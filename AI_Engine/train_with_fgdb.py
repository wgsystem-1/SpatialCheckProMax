"""FGDB 실제 데이터로 AI 모델 추가 훈련"""
import sys
import os
from pathlib import Path

# 경로 추가
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.chdir(os.path.dirname(os.path.abspath(__file__)))

import torch
import numpy as np
from datetime import datetime

from training.ai_training_pipeline import (
    GeometryGNN, GeometryDataset, FGDBGeometryDataset,
    Trainer, GeometryLoss, DEFAULT_CONFIG, set_seed,
    export_for_csharp, DEVICE
)


def find_fgdb_files(search_paths: list = None) -> list:
    """FGDB 파일 검색"""
    if search_paths is None:
        search_paths = [
            Path("G:/#Project"),
            Path("C:/GIS"),
            Path("D:/GIS"),
            Path.home() / "Documents",
        ]

    fgdb_files = []
    for base_path in search_paths:
        if base_path.exists():
            for gdb_path in base_path.rglob("*.gdb"):
                if gdb_path.is_dir():
                    fgdb_files.append(str(gdb_path))

    return fgdb_files


def train_with_synthetic_data(config: dict, epochs: int = 100):
    """합성 데이터로 기본 훈련"""
    print("=" * 60)
    print("Phase 1: Training with Synthetic Data")
    print("=" * 60)

    set_seed(42)

    # 데이터셋 생성
    train_dataset = GeometryDataset(
        n_samples=5000,
        max_vertices=config["max_vertices"],
        noise_range=config["noise_range"],
        include_topology_errors=True
    )

    val_dataset = GeometryDataset(
        n_samples=500,
        max_vertices=config["max_vertices"],
        noise_range=config["noise_range"],
        include_topology_errors=True
    )

    print(f"Training samples: {len(train_dataset)}")
    print(f"Validation samples: {len(val_dataset)}")

    # 모델 생성
    model = GeometryGNN(
        input_dim=config["input_dim"],
        hidden_dim=config["hidden_dim"],
        num_layers=config["num_layers"],
        dropout=config["dropout"]
    )

    print(f"Model parameters: {sum(p.numel() for p in model.parameters()):,}")
    print(f"Device: {DEVICE}")

    # DataLoader 생성
    train_loader = torch.utils.data.DataLoader(
        train_dataset,
        batch_size=config["batch_size"],
        shuffle=True,
        num_workers=0
    )

    val_loader = torch.utils.data.DataLoader(
        val_dataset,
        batch_size=config["batch_size"],
        shuffle=False,
        num_workers=0
    )

    # 트레이너 생성 및 훈련
    trainer = Trainer(
        model=model,
        config=config,
        save_dir="checkpoints"
    )

    trainer.train(train_loader, val_loader, epochs=epochs)

    return model, trainer


def train_with_fgdb_data(model: torch.nn.Module, fgdb_paths: list, config: dict, epochs: int = 50):
    """FGDB 실제 데이터로 추가 훈련 (Fine-tuning)"""
    print()
    print("=" * 60)
    print("Phase 2: Fine-tuning with FGDB Data")
    print("=" * 60)

    if not fgdb_paths:
        print("No FGDB files found. Skipping FGDB training.")
        return model

    print(f"Found {len(fgdb_paths)} FGDB file(s)")

    combined_samples = []

    for gdb_path in fgdb_paths:
        print(f"\nLoading: {gdb_path}")
        try:
            dataset = FGDBGeometryDataset(
                fgdb_path=gdb_path,
                layer_names=None,  # 모든 레이어
                noise_range=config["noise_range"],
                max_vertices=config["max_vertices"]
            )

            if len(dataset) > 0:
                print(f"  Loaded {len(dataset)} samples")
                combined_samples.extend(dataset.data)
            else:
                print("  No valid samples found")

        except Exception as e:
            print(f"  Error loading: {e}")

    if not combined_samples:
        print("\nNo FGDB samples loaded. Skipping fine-tuning.")
        return model

    print(f"\nTotal FGDB samples: {len(combined_samples)}")

    # 데이터셋 분할
    np.random.shuffle(combined_samples)
    split_idx = int(len(combined_samples) * 0.9)

    train_dataset = FGDBDatasetWrapper(combined_samples[:split_idx], config["max_vertices"])
    val_dataset = FGDBDatasetWrapper(combined_samples[split_idx:], config["max_vertices"])

    print(f"Training samples: {len(train_dataset)}")
    print(f"Validation samples: {len(val_dataset)}")

    # Fine-tuning (낮은 학습률)
    finetune_config = config.copy()
    finetune_config["learning_rate"] = config["learning_rate"] * 0.1

    # DataLoader 생성
    train_loader = torch.utils.data.DataLoader(
        train_dataset,
        batch_size=finetune_config["batch_size"],
        shuffle=True,
        num_workers=0
    )

    val_loader = torch.utils.data.DataLoader(
        val_dataset,
        batch_size=finetune_config["batch_size"],
        shuffle=False,
        num_workers=0
    )

    trainer = Trainer(
        model=model,
        config=finetune_config,
        save_dir="checkpoints"
    )

    trainer.train(train_loader, val_loader, epochs=epochs)

    return model


class FGDBDatasetWrapper(torch.utils.data.Dataset):
    """FGDB 샘플 래퍼 데이터셋"""
    def __init__(self, samples: list, max_vertices: int):
        self.samples = samples
        self.max_vertices = max_vertices

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        sample = self.samples[idx]
        noisy = sample["noisy_coords"]
        offsets = sample["offsets"]

        n_vertices = len(noisy)

        # 패딩
        padded_coords = np.zeros((self.max_vertices, 2), dtype=np.float32)
        padded_offsets = np.zeros((self.max_vertices, 2), dtype=np.float32)
        mask = np.zeros(self.max_vertices, dtype=np.float32)

        padded_coords[:n_vertices] = noisy
        padded_offsets[:n_vertices] = offsets
        mask[:n_vertices] = 1.0

        return {
            "input": torch.tensor(padded_coords),
            "target": torch.tensor(padded_offsets),
            "mask": torch.tensor(mask),
            "n_vertices": n_vertices
        }


def main():
    """메인 훈련 파이프라인"""
    print("=" * 60)
    print("SpatialCheckProMax AI Model Training")
    print(f"Started at: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)
    print()

    # 설정
    config = DEFAULT_CONFIG.copy()
    config["epochs"] = 100
    config["batch_size"] = 32

    # Phase 1: 합성 데이터 훈련
    model, trainer = train_with_synthetic_data(config, epochs=100)

    # Phase 2: FGDB 데이터로 Fine-tuning (옵션)
    fgdb_files = find_fgdb_files()
    if fgdb_files:
        print(f"\nFound FGDB files:")
        for f in fgdb_files[:5]:
            print(f"  - {f}")
        if len(fgdb_files) > 5:
            print(f"  ... and {len(fgdb_files) - 5} more")

        # 첫 번째 파일만 사용 (테스트용)
        model = train_with_fgdb_data(model, fgdb_files[:1], config, epochs=50)
    else:
        print("\nNo FGDB files found for fine-tuning.")

    # 모델 내보내기
    print()
    print("=" * 60)
    print("Exporting Model to ONNX")
    print("=" * 60)

    # 체크포인트에서 내보내기
    export_for_csharp(
        checkpoint_path="checkpoints/best_model.pt",
        output_dir="models",
        config=config
    )

    print()
    print("=" * 60)
    print("Training Complete!")
    print(f"Finished at: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)


if __name__ == "__main__":
    main()
