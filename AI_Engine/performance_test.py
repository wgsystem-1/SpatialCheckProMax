"""성능 테스트 스크립트 - AI 모델 및 지오메트리 처리 성능 검증"""
import sys
import os
import time
import json
from pathlib import Path

# 경로 추가
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.chdir(os.path.dirname(os.path.abspath(__file__)))

import numpy as np

# PyTorch와 ONNX Runtime 가용성 확인
try:
    import torch
    TORCH_AVAILABLE = True
except ImportError:
    TORCH_AVAILABLE = False
    print("Warning: PyTorch not available")

try:
    import onnxruntime as ort
    ONNX_AVAILABLE = True
except ImportError:
    ONNX_AVAILABLE = False
    print("Warning: ONNX Runtime not available")

try:
    from shapely.geometry import Polygon, LineString, Point
    from shapely import wkt
    SHAPELY_AVAILABLE = True
except ImportError:
    SHAPELY_AVAILABLE = False
    print("Warning: Shapely not available")


class PerformanceTestResult:
    """성능 테스트 결과"""
    def __init__(self, name: str):
        self.name = name
        self.times = []
        self.memory_before = 0
        self.memory_after = 0
        self.success = True
        self.error = None

    def add_time(self, elapsed: float):
        self.times.append(elapsed)

    @property
    def avg_time(self) -> float:
        return sum(self.times) / len(self.times) if self.times else 0

    @property
    def min_time(self) -> float:
        return min(self.times) if self.times else 0

    @property
    def max_time(self) -> float:
        return max(self.times) if self.times else 0

    @property
    def std_time(self) -> float:
        if len(self.times) < 2:
            return 0
        avg = self.avg_time
        return (sum((t - avg) ** 2 for t in self.times) / len(self.times)) ** 0.5

    @property
    def memory_used_mb(self) -> float:
        return (self.memory_after - self.memory_before) / (1024 * 1024)

    def to_dict(self) -> dict:
        return {
            "name": self.name,
            "success": self.success,
            "error": self.error,
            "iterations": len(self.times),
            "avg_time_ms": self.avg_time * 1000,
            "min_time_ms": self.min_time * 1000,
            "max_time_ms": self.max_time * 1000,
            "std_time_ms": self.std_time * 1000,
            "memory_used_mb": self.memory_used_mb
        }


def get_memory_usage() -> int:
    """현재 프로세스 메모리 사용량 (bytes)"""
    try:
        import psutil
        process = psutil.Process(os.getpid())
        return process.memory_info().rss
    except ImportError:
        return 0


def generate_test_geometries(count: int, vertices_per_geom: int = 50) -> list:
    """테스트용 지오메트리 생성"""
    geometries = []

    for i in range(count):
        # 랜덤 폴리곤 생성
        center_x = np.random.uniform(0, 1000)
        center_y = np.random.uniform(0, 1000)
        radius = np.random.uniform(10, 100)

        angles = np.linspace(0, 2 * np.pi, vertices_per_geom, endpoint=False)
        # 약간의 노이즈 추가
        radii = radius + np.random.uniform(-radius * 0.2, radius * 0.2, vertices_per_geom)

        coords = [
            (center_x + r * np.cos(a), center_y + r * np.sin(a))
            for a, r in zip(angles, radii)
        ]
        coords.append(coords[0])  # 폐합

        if SHAPELY_AVAILABLE:
            poly = Polygon(coords)
            if poly.is_valid:
                geometries.append(poly)
        else:
            geometries.append(coords)

    return geometries


def test_geometry_generation(iterations: int = 10, geom_count: int = 1000) -> PerformanceTestResult:
    """지오메트리 생성 성능 테스트"""
    result = PerformanceTestResult("Geometry Generation")
    result.memory_before = get_memory_usage()

    try:
        for _ in range(iterations):
            start = time.perf_counter()
            geometries = generate_test_geometries(geom_count)
            elapsed = time.perf_counter() - start
            result.add_time(elapsed)

        result.memory_after = get_memory_usage()
    except Exception as e:
        result.success = False
        result.error = str(e)

    return result


def test_ai_model_inference(iterations: int = 100, batch_size: int = 1, vertices: int = 100) -> PerformanceTestResult:
    """AI 모델 추론 성능 테스트"""
    result = PerformanceTestResult(f"AI Model Inference (batch={batch_size}, vertices={vertices})")

    if not ONNX_AVAILABLE:
        result.success = False
        result.error = "ONNX Runtime not available"
        return result

    model_path = Path("models/geometry_corrector.onnx")
    if not model_path.exists():
        result.success = False
        result.error = f"Model not found: {model_path}"
        return result

    result.memory_before = get_memory_usage()

    try:
        # 세션 로드
        session = ort.InferenceSession(str(model_path))

        # 더미 입력 생성 (모델의 max_vertices=500)
        max_vertices = 500
        coords = np.random.randn(batch_size, max_vertices, 2).astype(np.float32)
        mask = np.zeros((batch_size, max_vertices), dtype=np.float32)
        mask[:, :vertices] = 1.0  # 실제 정점만 마스크

        # Warmup
        for _ in range(5):
            session.run(None, {"coordinates": coords, "mask": mask})

        # 실제 테스트
        for _ in range(iterations):
            start = time.perf_counter()
            outputs = session.run(None, {"coordinates": coords, "mask": mask})
            elapsed = time.perf_counter() - start
            result.add_time(elapsed)

        result.memory_after = get_memory_usage()

    except Exception as e:
        result.success = False
        result.error = str(e)

    return result


def test_ai_model_batch_scaling() -> list:
    """AI 모델 배치 크기별 성능 테스트"""
    results = []

    batch_sizes = [1, 2, 4, 8, 16, 32]
    for batch_size in batch_sizes:
        result = test_ai_model_inference(iterations=50, batch_size=batch_size, vertices=100)
        results.append(result)

        if result.success:
            throughput = batch_size / result.avg_time
            print(f"  Batch {batch_size:2d}: {result.avg_time*1000:6.2f}ms avg, "
                  f"{throughput:.1f} geoms/sec")

    return results


def test_ai_model_vertex_scaling() -> list:
    """AI 모델 정점 수별 성능 테스트"""
    results = []

    vertex_counts = [10, 50, 100, 200, 300, 400, 500]
    for vertices in vertex_counts:
        result = test_ai_model_inference(iterations=50, batch_size=1, vertices=vertices)
        results.append(result)

        if result.success:
            print(f"  Vertices {vertices:3d}: {result.avg_time*1000:6.2f}ms avg")

    return results


def test_pytorch_training_speed(epochs: int = 10) -> PerformanceTestResult:
    """PyTorch 훈련 속도 테스트"""
    result = PerformanceTestResult(f"PyTorch Training ({epochs} epochs)")

    if not TORCH_AVAILABLE:
        result.success = False
        result.error = "PyTorch not available"
        return result

    result.memory_before = get_memory_usage()

    try:
        from training.ai_training_pipeline import GeometryGNN, GeometryDataset, DEFAULT_CONFIG

        # 작은 데이터셋으로 테스트
        model = GeometryGNN(
            input_dim=DEFAULT_CONFIG["input_dim"],
            hidden_dim=64,  # 작게
            num_layers=2,
            dropout=0.1
        )

        dataset = GeometryDataset(
            n_samples=100,
            noise_range=(0.05, 0.2),
            max_vertices=50
        )

        dataloader = torch.utils.data.DataLoader(dataset, batch_size=16, shuffle=True)
        optimizer = torch.optim.Adam(model.parameters(), lr=0.001)
        criterion = torch.nn.MSELoss()

        model.train()

        for epoch in range(epochs):
            epoch_start = time.perf_counter()

            for batch in dataloader:
                coords = batch["coordinates"]
                target = batch["target_offsets"]
                mask = batch["mask"]

                optimizer.zero_grad()
                output = model(coords, mask)
                loss = criterion(output * mask.unsqueeze(-1), target * mask.unsqueeze(-1))
                loss.backward()
                optimizer.step()

            epoch_time = time.perf_counter() - epoch_start
            result.add_time(epoch_time)

        result.memory_after = get_memory_usage()

    except Exception as e:
        result.success = False
        result.error = str(e)

    return result


def run_all_tests():
    """모든 성능 테스트 실행"""
    print("=" * 60)
    print("SpatialCheckProMax Performance Test Suite")
    print("=" * 60)
    print()

    all_results = []

    # 1. 지오메트리 생성 테스트
    print("[1/5] Testing Geometry Generation...")
    result = test_geometry_generation(iterations=5, geom_count=1000)
    all_results.append(result)
    if result.success:
        print(f"  - {result.avg_time*1000:.2f}ms avg for 1000 geometries")
        print(f"  - Memory used: {result.memory_used_mb:.2f} MB")
    else:
        print(f"  - FAILED: {result.error}")
    print()

    # 2. AI 모델 추론 테스트
    print("[2/5] Testing AI Model Inference...")
    result = test_ai_model_inference(iterations=100, batch_size=1, vertices=100)
    all_results.append(result)
    if result.success:
        print(f"  - {result.avg_time*1000:.2f}ms avg (single inference)")
        print(f"  - Throughput: {1/result.avg_time:.1f} inferences/sec")
    else:
        print(f"  - FAILED: {result.error}")
    print()

    # 3. 배치 크기별 스케일링
    print("[3/5] Testing Batch Size Scaling...")
    batch_results = test_ai_model_batch_scaling()
    all_results.extend(batch_results)
    print()

    # 4. 정점 수별 스케일링
    print("[4/5] Testing Vertex Count Scaling...")
    vertex_results = test_ai_model_vertex_scaling()
    all_results.extend(vertex_results)
    print()

    # 5. PyTorch 훈련 속도
    print("[5/5] Testing PyTorch Training Speed...")
    result = test_pytorch_training_speed(epochs=5)
    all_results.append(result)
    if result.success:
        print(f"  - {result.avg_time*1000:.2f}ms avg per epoch")
        print(f"  - Memory used: {result.memory_used_mb:.2f} MB")
    else:
        print(f"  - FAILED: {result.error}")
    print()

    # 결과 요약
    print("=" * 60)
    print("SUMMARY")
    print("=" * 60)

    success_count = sum(1 for r in all_results if r.success)
    total_count = len(all_results)
    print(f"Tests passed: {success_count}/{total_count}")

    # 핵심 성능 지표
    for result in all_results:
        if result.success and "Inference" in result.name and "batch=1" in result.name:
            print(f"\nAI Model Performance:")
            print(f"  - Single inference: {result.avg_time*1000:.2f}ms")
            print(f"  - Throughput: {1/result.avg_time:.0f} inferences/sec")

            # 실제 사용 시나리오 추정
            geoms_per_second = 1/result.avg_time
            print(f"\nEstimated Processing Time:")
            print(f"  - 1,000 geometries: {1000/geoms_per_second:.1f} sec")
            print(f"  - 10,000 geometries: {10000/geoms_per_second:.1f} sec")
            print(f"  - 100,000 geometries: {100000/geoms_per_second/60:.1f} min")

    # 결과 저장
    results_path = Path("performance_results.json")
    with open(results_path, "w", encoding="utf-8") as f:
        json.dump([r.to_dict() for r in all_results], f, indent=2, ensure_ascii=False)
    print(f"\nResults saved to: {results_path}")

    return all_results


if __name__ == "__main__":
    run_all_tests()
