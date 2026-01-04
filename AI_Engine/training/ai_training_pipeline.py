"""
SpatialCheckProMax AI Training Pipeline
========================================
로컬 실행 전용 - 외부 API 사용 없음

모델: GeometryGNN (Graph Neural Network for Geometry Correction)
프레임워크: PyTorch → ONNX (C# 연동용)
용도: 지오메트리 오류 자동 수정 (정점 좌표 보정)

주요 기능:
1. 노이즈 주입을 통한 합성 데이터 생성
2. Graph Neural Network 기반 정점 보정 학습
3. ONNX 형식으로 내보내기 (C# OnnxRuntime 연동)
"""

import os
import json
import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import Dataset, DataLoader
from shapely.geometry import Polygon, LineString, Point, MultiPolygon
from shapely import wkt
from typing import List, Tuple, Dict, Optional
import random
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------
# 1. 설정 및 상수
# ---------------------------------------------------------

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
RANDOM_SEED = 42

# 기본 하이퍼파라미터
DEFAULT_CONFIG = {
    "input_dim": 2,           # x, y 좌표
    "hidden_dim": 128,        # 은닉층 크기
    "num_layers": 3,          # GNN 레이어 수
    "dropout": 0.1,           # 드롭아웃 비율
    "learning_rate": 0.001,   # 학습률
    "batch_size": 32,         # 배치 크기
    "epochs": 100,            # 에폭 수
    "noise_range": (0.05, 0.3),  # 노이즈 범위 (미터)
    "max_vertices": 500,      # 최대 정점 수
}

def set_seed(seed: int = RANDOM_SEED):
    """재현성을 위한 시드 설정"""
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)

# ---------------------------------------------------------
# 2. 데이터 합성 모듈
# ---------------------------------------------------------

def inject_vertex_noise(
    geometry,
    noise_range: Tuple[float, float] = (0.05, 0.3)
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """
    지오메트리에 노이즈를 주입하고 (원본, 노이즈, 오프셋) 반환

    Args:
        geometry: Shapely geometry (Polygon, LineString)
        noise_range: (min, max) 노이즈 범위 (미터)

    Returns:
        clean_coords: 원본 좌표 [N, 2]
        noisy_coords: 노이즈 적용 좌표 [N, 2]
        offsets: 보정 오프셋 (clean - noisy) [N, 2]
    """
    if isinstance(geometry, Polygon):
        coords = np.array(geometry.exterior.coords[:-1])  # 폐합점 제외
    elif isinstance(geometry, LineString):
        coords = np.array(geometry.coords)
    else:
        raise ValueError(f"Unsupported geometry type: {type(geometry)}")

    n_points = len(coords)
    clean_coords = coords.copy()

    # 각 정점에 랜덤 노이즈 적용
    angles = np.random.uniform(0, 2 * np.pi, n_points)
    distances = np.random.uniform(noise_range[0], noise_range[1], n_points)

    dx = np.cos(angles) * distances
    dy = np.sin(angles) * distances
    noise = np.stack([dx, dy], axis=1)

    noisy_coords = clean_coords + noise
    offsets = clean_coords - noisy_coords  # 보정해야 할 오프셋

    return clean_coords.astype(np.float32), noisy_coords.astype(np.float32), offsets.astype(np.float32)


def create_topology_errors(
    geometry,
    error_type: str = "random",
    error_magnitude: float = 0.05
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """
    토폴로지 오류 생성 (Gap, Overlap, Spike 등)

    Args:
        geometry: 원본 지오메트리
        error_type: "gap", "overlap", "spike", "random"
        error_magnitude: 오류 크기 (미터)

    Returns:
        clean_coords, error_coords, offsets
    """
    if error_type == "random":
        error_type = random.choice(["gap", "overlap", "spike", "shift"])

    if isinstance(geometry, Polygon):
        coords = np.array(geometry.exterior.coords[:-1])
    elif isinstance(geometry, LineString):
        coords = np.array(geometry.coords)
    else:
        raise ValueError(f"Unsupported geometry type: {type(geometry)}")

    clean_coords = coords.copy().astype(np.float32)
    error_coords = coords.copy().astype(np.float32)

    n_points = len(coords)

    if error_type == "gap":
        # 일부 정점을 안쪽으로 이동 (간극 생성)
        affected_indices = random.sample(range(n_points), max(1, n_points // 4))
        for idx in affected_indices:
            direction = np.random.randn(2)
            direction = direction / (np.linalg.norm(direction) + 1e-8)
            error_coords[idx] -= direction * error_magnitude

    elif error_type == "overlap":
        # 일부 정점을 바깥으로 이동 (중첩 생성)
        affected_indices = random.sample(range(n_points), max(1, n_points // 4))
        for idx in affected_indices:
            direction = np.random.randn(2)
            direction = direction / (np.linalg.norm(direction) + 1e-8)
            error_coords[idx] += direction * error_magnitude

    elif error_type == "spike":
        # 스파이크 오류 생성 (날카로운 돌출)
        if n_points >= 3:
            idx = random.randint(1, n_points - 2)
            # 이전/다음 정점의 중간 방향으로 돌출
            prev_pt = error_coords[idx - 1]
            next_pt = error_coords[(idx + 1) % n_points]
            mid_direction = (prev_pt + next_pt) / 2 - error_coords[idx]
            mid_direction = mid_direction / (np.linalg.norm(mid_direction) + 1e-8)
            error_coords[idx] -= mid_direction * error_magnitude * 3  # 더 큰 돌출

    elif error_type == "shift":
        # 전체 이동 (좌표계 오류 시뮬레이션)
        shift = np.random.uniform(-error_magnitude, error_magnitude, 2)
        error_coords += shift

    offsets = clean_coords - error_coords
    return clean_coords, error_coords, offsets.astype(np.float32)


def generate_synthetic_polygon(
    center: Tuple[float, float] = (0, 0),
    radius_range: Tuple[float, float] = (10, 100),
    n_vertices_range: Tuple[int, int] = (4, 20),
    irregularity: float = 0.3
) -> Polygon:
    """합성 폴리곤 생성"""
    n_vertices = random.randint(*n_vertices_range)
    base_radius = random.uniform(*radius_range)

    angles = np.linspace(0, 2 * np.pi, n_vertices, endpoint=False)
    angles += np.random.uniform(-irregularity, irregularity, n_vertices)

    radii = base_radius * (1 + np.random.uniform(-irregularity, irregularity, n_vertices))

    x = center[0] + radii * np.cos(angles)
    y = center[1] + radii * np.sin(angles)

    coords = list(zip(x, y))
    coords.append(coords[0])  # 폐합

    return Polygon(coords)


def generate_synthetic_line(
    start: Tuple[float, float] = (0, 0),
    length_range: Tuple[float, float] = (50, 200),
    n_vertices_range: Tuple[int, int] = (3, 15),
    curvature: float = 0.3
) -> LineString:
    """합성 선형 생성"""
    n_vertices = random.randint(*n_vertices_range)
    length = random.uniform(*length_range)

    # 기본 방향
    direction = np.random.rand(2)
    direction = direction / np.linalg.norm(direction)

    coords = [start]
    current = np.array(start)
    segment_length = length / (n_vertices - 1)

    for _ in range(n_vertices - 1):
        # 방향에 약간의 변화 추가
        direction += np.random.uniform(-curvature, curvature, 2)
        direction = direction / np.linalg.norm(direction)
        current = current + direction * segment_length
        coords.append(tuple(current))

    return LineString(coords)


# ---------------------------------------------------------
# 3. 데이터셋 클래스
# ---------------------------------------------------------

class GeometryDataset(Dataset):
    """지오메트리 보정 학습용 데이터셋"""

    def __init__(
        self,
        n_samples: int = 10000,
        max_vertices: int = 500,
        noise_range: Tuple[float, float] = (0.05, 0.3),
        include_topology_errors: bool = True,
        geometry_types: List[str] = ["polygon", "line"]
    ):
        self.n_samples = n_samples
        self.max_vertices = max_vertices
        self.noise_range = noise_range
        self.include_topology_errors = include_topology_errors
        self.geometry_types = geometry_types

        self.data = []
        self._generate_samples()

    def _generate_samples(self):
        """합성 샘플 생성"""
        for _ in range(self.n_samples):
            geom_type = random.choice(self.geometry_types)

            if geom_type == "polygon":
                geometry = generate_synthetic_polygon()
            else:
                geometry = generate_synthetic_line()

            # 노이즈 또는 토폴로지 오류 적용
            if self.include_topology_errors and random.random() < 0.3:
                clean, noisy, offsets = create_topology_errors(geometry)
            else:
                clean, noisy, offsets = inject_vertex_noise(geometry, self.noise_range)

            self.data.append({
                "clean": clean,
                "noisy": noisy,
                "offsets": offsets,
                "n_vertices": len(clean)
            })

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        sample = self.data[idx]

        # 패딩 적용 (고정 크기로)
        n = sample["n_vertices"]
        padded_noisy = np.zeros((self.max_vertices, 2), dtype=np.float32)
        padded_offsets = np.zeros((self.max_vertices, 2), dtype=np.float32)
        mask = np.zeros(self.max_vertices, dtype=np.float32)

        padded_noisy[:n] = sample["noisy"]
        padded_offsets[:n] = sample["offsets"]
        mask[:n] = 1.0

        return {
            "input": torch.from_numpy(padded_noisy),
            "target": torch.from_numpy(padded_offsets),
            "mask": torch.from_numpy(mask),
            "n_vertices": n
        }


class FGDBGeometryDataset(Dataset):
    """
    FGDB에서 실제 지오메트리를 로드하는 데이터셋
    (GDAL/OGR 필요)
    """

    def __init__(
        self,
        fgdb_path: str,
        layer_names: Optional[List[str]] = None,
        max_vertices: int = 500,
        noise_range: Tuple[float, float] = (0.05, 0.3)
    ):
        self.fgdb_path = fgdb_path
        self.max_vertices = max_vertices
        self.noise_range = noise_range

        self.geometries = []
        self._load_geometries(layer_names)

        self.data = []
        self._prepare_samples()

    def _load_geometries(self, layer_names: Optional[List[str]]):
        """FGDB에서 지오메트리 로드"""
        try:
            from osgeo import ogr
            ogr.UseExceptions()

            ds = ogr.Open(self.fgdb_path, 0)
            if ds is None:
                print(f"Warning: Cannot open {self.fgdb_path}")
                return

            layers_to_process = layer_names or [
                ds.GetLayerByIndex(i).GetName()
                for i in range(ds.GetLayerCount())
            ]

            for layer_name in layers_to_process:
                layer = ds.GetLayerByName(layer_name)
                if layer is None:
                    continue

                for feature in layer:
                    geom = feature.GetGeometryRef()
                    if geom is None:
                        continue

                    geom_wkt = geom.ExportToWkt()
                    try:
                        shapely_geom = wkt.loads(geom_wkt)
                        if isinstance(shapely_geom, (Polygon, LineString)):
                            self.geometries.append(shapely_geom)
                        elif isinstance(shapely_geom, MultiPolygon):
                            for poly in shapely_geom.geoms:
                                self.geometries.append(poly)
                    except Exception:
                        continue

            ds = None
            print(f"Loaded {len(self.geometries)} geometries from FGDB")

        except ImportError:
            print("Warning: GDAL not available, using synthetic data only")
        except Exception as e:
            print(f"Warning: Error loading FGDB: {e}")

    def _prepare_samples(self):
        """로드된 지오메트리에서 훈련 샘플 생성"""
        for geom in self.geometries:
            try:
                # 정점 수 확인
                if isinstance(geom, Polygon):
                    n_vertices = len(geom.exterior.coords) - 1
                else:
                    n_vertices = len(geom.coords)

                if n_vertices > self.max_vertices or n_vertices < 3:
                    continue

                clean, noisy, offsets = inject_vertex_noise(geom, self.noise_range)

                self.data.append({
                    "clean": clean,
                    "noisy": noisy,
                    "offsets": offsets,
                    "n_vertices": len(clean)
                })
            except Exception:
                continue

        print(f"Prepared {len(self.data)} training samples")

    def __len__(self):
        return len(self.data) if self.data else 1

    def __getitem__(self, idx):
        if not self.data:
            # 빈 데이터셋인 경우 더미 반환
            return {
                "input": torch.zeros(self.max_vertices, 2),
                "target": torch.zeros(self.max_vertices, 2),
                "mask": torch.zeros(self.max_vertices),
                "n_vertices": 0
            }

        sample = self.data[idx]
        n = sample["n_vertices"]

        padded_noisy = np.zeros((self.max_vertices, 2), dtype=np.float32)
        padded_offsets = np.zeros((self.max_vertices, 2), dtype=np.float32)
        mask = np.zeros(self.max_vertices, dtype=np.float32)

        padded_noisy[:n] = sample["noisy"]
        padded_offsets[:n] = sample["offsets"]
        mask[:n] = 1.0

        return {
            "input": torch.from_numpy(padded_noisy),
            "target": torch.from_numpy(padded_offsets),
            "mask": torch.from_numpy(mask),
            "n_vertices": n
        }


# ---------------------------------------------------------
# 4. Graph Neural Network 모델
# ---------------------------------------------------------

class GraphConvLayer(nn.Module):
    """그래프 컨볼루션 레이어 (정점 간 관계 학습)"""

    def __init__(self, in_dim: int, out_dim: int):
        super().__init__()
        self.linear = nn.Linear(in_dim, out_dim)
        self.neighbor_linear = nn.Linear(in_dim, out_dim)

    def forward(self, x: torch.Tensor, mask: torch.Tensor) -> torch.Tensor:
        """
        Args:
            x: [batch, n_nodes, in_dim]
            mask: [batch, n_nodes]
        Returns:
            [batch, n_nodes, out_dim]
        """
        batch_size, n_nodes, _ = x.shape

        # 자기 변환
        self_transform = self.linear(x)

        # 이웃 집계 (순환 구조: 이전/다음 정점)
        # 이전 정점
        prev_x = torch.roll(x, shifts=1, dims=1)
        # 다음 정점
        next_x = torch.roll(x, shifts=-1, dims=1)

        # 이웃 평균
        neighbor_sum = prev_x + next_x
        neighbor_transform = self.neighbor_linear(neighbor_sum)

        # 마스크 적용
        mask_expanded = mask.unsqueeze(-1)
        output = (self_transform + neighbor_transform) * mask_expanded

        return output


class GeometryGNN(nn.Module):
    """
    지오메트리 보정을 위한 Graph Neural Network

    입력: 노이즈/오류가 있는 정점 좌표 [batch, n_nodes, 2]
    출력: 보정 오프셋 (dx, dy) [batch, n_nodes, 2]
    """

    def __init__(
        self,
        input_dim: int = 2,
        hidden_dim: int = 128,
        num_layers: int = 3,
        dropout: float = 0.1
    ):
        super().__init__()

        self.input_dim = input_dim
        self.hidden_dim = hidden_dim
        self.num_layers = num_layers

        # 입력 임베딩
        self.input_embed = nn.Linear(input_dim, hidden_dim)

        # Graph Convolution 레이어들
        self.conv_layers = nn.ModuleList([
            GraphConvLayer(hidden_dim, hidden_dim)
            for _ in range(num_layers)
        ])

        # 배치 정규화
        self.batch_norms = nn.ModuleList([
            nn.BatchNorm1d(hidden_dim)
            for _ in range(num_layers)
        ])

        # 드롭아웃
        self.dropout = nn.Dropout(dropout)

        # 출력 레이어
        self.output_layer = nn.Sequential(
            nn.Linear(hidden_dim, hidden_dim // 2),
            nn.ReLU(),
            nn.Linear(hidden_dim // 2, 2)  # dx, dy
        )

    def forward(
        self,
        x: torch.Tensor,
        mask: Optional[torch.Tensor] = None
    ) -> torch.Tensor:
        """
        Args:
            x: [batch, n_nodes, 2] 입력 좌표
            mask: [batch, n_nodes] 유효 정점 마스크

        Returns:
            [batch, n_nodes, 2] 보정 오프셋
        """
        batch_size, n_nodes, _ = x.shape

        if mask is None:
            mask = torch.ones(batch_size, n_nodes, device=x.device)

        # 입력 임베딩
        h = self.input_embed(x)

        # Graph Convolution 레이어 통과
        for i, (conv, bn) in enumerate(zip(self.conv_layers, self.batch_norms)):
            h_new = conv(h, mask)

            # BatchNorm (차원 변환 필요)
            h_new = h_new.transpose(1, 2)  # [batch, hidden, n_nodes]
            h_new = bn(h_new)
            h_new = h_new.transpose(1, 2)  # [batch, n_nodes, hidden]

            h_new = F.relu(h_new)
            h_new = self.dropout(h_new)

            # Residual connection
            h = h + h_new

        # 출력 (보정 오프셋)
        output = self.output_layer(h)

        # 마스크 적용
        output = output * mask.unsqueeze(-1)

        return output


# ---------------------------------------------------------
# 5. 손실 함수
# ---------------------------------------------------------

class GeometryLoss(nn.Module):
    """지오메트리 보정을 위한 복합 손실 함수"""

    def __init__(
        self,
        mse_weight: float = 1.0,
        smoothness_weight: float = 0.1,
        topology_weight: float = 0.1
    ):
        super().__init__()
        self.mse_weight = mse_weight
        self.smoothness_weight = smoothness_weight
        self.topology_weight = topology_weight

    def forward(
        self,
        pred_offsets: torch.Tensor,
        target_offsets: torch.Tensor,
        mask: torch.Tensor,
        input_coords: Optional[torch.Tensor] = None
    ) -> Dict[str, torch.Tensor]:
        """
        Args:
            pred_offsets: [batch, n_nodes, 2] 예측 오프셋
            target_offsets: [batch, n_nodes, 2] 실제 오프셋
            mask: [batch, n_nodes] 유효 정점 마스크
            input_coords: [batch, n_nodes, 2] 입력 좌표 (스무스니스 계산용)
        """
        # 마스크 확장
        mask_expanded = mask.unsqueeze(-1)

        # MSE 손실 (마스크 적용)
        mse_loss = F.mse_loss(
            pred_offsets * mask_expanded,
            target_offsets * mask_expanded,
            reduction='sum'
        ) / (mask.sum() + 1e-8)

        total_loss = self.mse_weight * mse_loss

        losses = {"mse": mse_loss}

        # 스무스니스 손실 (보정된 좌표의 부드러움)
        if input_coords is not None and self.smoothness_weight > 0:
            corrected = input_coords + pred_offsets
            # 이웃 정점과의 차이
            diff_prev = corrected - torch.roll(corrected, 1, dims=1)
            diff_next = corrected - torch.roll(corrected, -1, dims=1)
            smoothness = (diff_prev.pow(2) + diff_next.pow(2)) * mask_expanded
            smoothness_loss = smoothness.sum() / (mask.sum() + 1e-8)

            total_loss = total_loss + self.smoothness_weight * smoothness_loss
            losses["smoothness"] = smoothness_loss

        losses["total"] = total_loss
        return losses


# ---------------------------------------------------------
# 6. 훈련 루프
# ---------------------------------------------------------

class Trainer:
    """모델 훈련 클래스"""

    def __init__(
        self,
        model: nn.Module,
        config: Dict,
        save_dir: str = "checkpoints"
    ):
        self.model = model.to(DEVICE)
        self.config = config
        self.save_dir = Path(save_dir)
        self.save_dir.mkdir(parents=True, exist_ok=True)

        self.optimizer = torch.optim.AdamW(
            model.parameters(),
            lr=config.get("learning_rate", 0.001),
            weight_decay=0.01
        )

        self.scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
            self.optimizer,
            T_max=config.get("epochs", 100)
        )

        self.criterion = GeometryLoss()

        self.best_loss = float('inf')
        self.train_history = []
        self.val_history = []

    def train_epoch(self, train_loader: DataLoader) -> Dict[str, float]:
        """1 에폭 훈련"""
        self.model.train()
        total_loss = 0.0
        total_mse = 0.0
        n_batches = 0

        for batch in train_loader:
            inputs = batch["input"].to(DEVICE)
            targets = batch["target"].to(DEVICE)
            masks = batch["mask"].to(DEVICE)

            self.optimizer.zero_grad()

            outputs = self.model(inputs, masks)
            losses = self.criterion(outputs, targets, masks, inputs)

            losses["total"].backward()
            torch.nn.utils.clip_grad_norm_(self.model.parameters(), 1.0)
            self.optimizer.step()

            total_loss += losses["total"].item()
            total_mse += losses["mse"].item()
            n_batches += 1

        return {
            "loss": total_loss / n_batches,
            "mse": total_mse / n_batches
        }

    @torch.no_grad()
    def validate(self, val_loader: DataLoader) -> Dict[str, float]:
        """검증"""
        self.model.eval()
        total_loss = 0.0
        total_mse = 0.0
        n_batches = 0

        for batch in val_loader:
            inputs = batch["input"].to(DEVICE)
            targets = batch["target"].to(DEVICE)
            masks = batch["mask"].to(DEVICE)

            outputs = self.model(inputs, masks)
            losses = self.criterion(outputs, targets, masks, inputs)

            total_loss += losses["total"].item()
            total_mse += losses["mse"].item()
            n_batches += 1

        return {
            "loss": total_loss / n_batches,
            "mse": total_mse / n_batches
        }

    def train(
        self,
        train_loader: DataLoader,
        val_loader: Optional[DataLoader] = None,
        epochs: Optional[int] = None
    ):
        """전체 훈련 실행"""
        epochs = epochs or self.config.get("epochs", 100)

        print(f"Training on {DEVICE}")
        print(f"Epochs: {epochs}, Batch size: {train_loader.batch_size}")
        print("-" * 50)

        for epoch in range(epochs):
            train_metrics = self.train_epoch(train_loader)
            self.train_history.append(train_metrics)

            val_metrics = None
            if val_loader is not None:
                val_metrics = self.validate(val_loader)
                self.val_history.append(val_metrics)

            self.scheduler.step()

            # 로그
            log_str = f"Epoch {epoch+1}/{epochs} - "
            log_str += f"Train Loss: {train_metrics['loss']:.6f}, MSE: {train_metrics['mse']:.6f}"

            if val_metrics:
                log_str += f" | Val Loss: {val_metrics['loss']:.6f}, MSE: {val_metrics['mse']:.6f}"

            print(log_str)

            # 체크포인트 저장
            current_loss = val_metrics["loss"] if val_metrics else train_metrics["loss"]
            if current_loss < self.best_loss:
                self.best_loss = current_loss
                self.save_checkpoint(f"best_model.pt")

            # 주기적 저장
            if (epoch + 1) % 10 == 0:
                self.save_checkpoint(f"model_epoch_{epoch+1}.pt")

        print("-" * 50)
        print(f"Training complete. Best loss: {self.best_loss:.6f}")

    def save_checkpoint(self, filename: str):
        """체크포인트 저장"""
        checkpoint = {
            "model_state_dict": self.model.state_dict(),
            "optimizer_state_dict": self.optimizer.state_dict(),
            "config": self.config,
            "best_loss": self.best_loss,
            "train_history": self.train_history,
            "val_history": self.val_history
        }
        torch.save(checkpoint, self.save_dir / filename)

    def load_checkpoint(self, filepath: str):
        """체크포인트 로드"""
        checkpoint = torch.load(filepath, map_location=DEVICE)
        self.model.load_state_dict(checkpoint["model_state_dict"])
        self.optimizer.load_state_dict(checkpoint["optimizer_state_dict"])
        self.best_loss = checkpoint.get("best_loss", float('inf'))
        self.train_history = checkpoint.get("train_history", [])
        self.val_history = checkpoint.get("val_history", [])


# ---------------------------------------------------------
# 7. ONNX 내보내기
# ---------------------------------------------------------

def export_to_onnx(
    model: nn.Module,
    model_path: str = "geometry_corrector.onnx",
    max_vertices: int = 500,
    opset_version: int = 17
):
    """
    PyTorch 모델을 ONNX 형식으로 내보내기

    Args:
        model: 훈련된 GeometryGNN 모델
        model_path: 출력 ONNX 파일 경로
        max_vertices: 최대 정점 수
        opset_version: ONNX opset 버전
    """
    import os
    os.environ["PYTHONIOENCODING"] = "utf-8"

    model.eval()
    model.to("cpu")

    # 더미 입력
    dummy_input = torch.randn(1, max_vertices, 2)
    dummy_mask = torch.ones(1, max_vertices)

    # ONNX 내보내기 (dynamo=False로 레거시 방식 사용)
    torch.onnx.export(
        model,
        (dummy_input, dummy_mask),
        model_path,
        input_names=["coordinates", "mask"],
        output_names=["offsets"],
        dynamic_axes={
            "coordinates": {0: "batch_size", 1: "num_vertices"},
            "mask": {0: "batch_size", 1: "num_vertices"},
            "offsets": {0: "batch_size", 1: "num_vertices"}
        },
        opset_version=opset_version,
        do_constant_folding=True,
        dynamo=False,  # 레거시 TorchScript 방식 사용
        verbose=False
    )

    print(f"Model exported to {model_path}")

    # 검증
    try:
        import onnx
        onnx_model = onnx.load(model_path)
        onnx.checker.check_model(onnx_model)
        print("ONNX model validation passed")
    except ImportError:
        print("onnx package not installed, skipping validation")
    except Exception as e:
        print(f"ONNX validation warning: {e}")


def export_for_csharp(
    checkpoint_path: str,
    output_dir: str = "models",
    config: Optional[Dict] = None
):
    """
    C# OnnxRuntime 연동용 모델 패키지 생성

    Args:
        checkpoint_path: PyTorch 체크포인트 경로
        output_dir: 출력 디렉토리
        config: 모델 설정 (없으면 체크포인트에서 로드)
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    # 체크포인트 로드
    checkpoint = torch.load(checkpoint_path, map_location="cpu")

    if config is None:
        config = checkpoint.get("config", DEFAULT_CONFIG)

    # 모델 재구성
    model = GeometryGNN(
        input_dim=config.get("input_dim", 2),
        hidden_dim=config.get("hidden_dim", 128),
        num_layers=config.get("num_layers", 3),
        dropout=0.0  # 추론 시 드롭아웃 비활성화
    )
    model.load_state_dict(checkpoint["model_state_dict"])

    # ONNX 내보내기
    onnx_path = output_dir / "geometry_corrector.onnx"
    export_to_onnx(model, str(onnx_path), config.get("max_vertices", 500))

    # 메타데이터 저장
    metadata = {
        "model_name": "GeometryGNN",
        "version": "1.0.0",
        "created": datetime.now().isoformat(),
        "config": config,
        "input_format": {
            "coordinates": "[batch, num_vertices, 2] float32 - (x, y) 좌표",
            "mask": "[batch, num_vertices] float32 - 유효 정점 마스크 (1=유효, 0=패딩)"
        },
        "output_format": {
            "offsets": "[batch, num_vertices, 2] float32 - (dx, dy) 보정 오프셋"
        },
        "usage": "corrected_coords = input_coords + offsets"
    }

    with open(output_dir / "model_metadata.json", "w", encoding="utf-8") as f:
        json.dump(metadata, f, indent=2, ensure_ascii=False)

    print(f"Model package exported to {output_dir}")
    print(f"  - ONNX model: {onnx_path}")
    print(f"  - Metadata: {output_dir / 'model_metadata.json'}")


# ---------------------------------------------------------
# 8. 메인 실행
# ---------------------------------------------------------

def main():
    """메인 훈련 파이프라인"""
    set_seed(RANDOM_SEED)

    config = DEFAULT_CONFIG.copy()

    print("=" * 60)
    print("SpatialCheckProMax AI Training Pipeline")
    print("=" * 60)
    print(f"Device: {DEVICE}")
    print(f"Config: {json.dumps(config, indent=2)}")
    print()

    # 1. 데이터셋 생성
    print("Creating synthetic dataset...")
    train_dataset = GeometryDataset(
        n_samples=8000,
        max_vertices=config["max_vertices"],
        noise_range=config["noise_range"]
    )

    val_dataset = GeometryDataset(
        n_samples=2000,
        max_vertices=config["max_vertices"],
        noise_range=config["noise_range"]
    )

    train_loader = DataLoader(
        train_dataset,
        batch_size=config["batch_size"],
        shuffle=True,
        num_workers=0
    )

    val_loader = DataLoader(
        val_dataset,
        batch_size=config["batch_size"],
        shuffle=False,
        num_workers=0
    )

    print(f"Training samples: {len(train_dataset)}")
    print(f"Validation samples: {len(val_dataset)}")
    print()

    # 2. 모델 생성
    print("Creating model...")
    model = GeometryGNN(
        input_dim=config["input_dim"],
        hidden_dim=config["hidden_dim"],
        num_layers=config["num_layers"],
        dropout=config["dropout"]
    )

    total_params = sum(p.numel() for p in model.parameters())
    trainable_params = sum(p.numel() for p in model.parameters() if p.requires_grad)
    print(f"Total parameters: {total_params:,}")
    print(f"Trainable parameters: {trainable_params:,}")
    print()

    # 3. 훈련
    print("Starting training...")
    trainer = Trainer(model, config, save_dir="AI_Engine/checkpoints")
    trainer.train(train_loader, val_loader, epochs=config["epochs"])

    # 4. ONNX 내보내기
    print()
    print("Exporting model for C# integration...")
    export_for_csharp(
        "AI_Engine/checkpoints/best_model.pt",
        output_dir="AI_Engine/models",
        config=config
    )

    print()
    print("=" * 60)
    print("Training complete!")
    print("=" * 60)


if __name__ == "__main__":
    main()
