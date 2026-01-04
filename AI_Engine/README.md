# AI Engine - GeometryGNN 모델 훈련 가이드

## 개요

SpatialCheckProMax AI Engine은 지오메트리 오류를 자동 수정하는 Graph Neural Network (GNN) 기반 모델입니다.

### 모델 사양

| 항목 | 값 |
|------|-----|
| 모델명 | GeometryGNN |
| 프레임워크 | PyTorch → ONNX |
| 입력 | coordinates [batch, num_vertices, 2], mask [batch, num_vertices] |
| 출력 | offsets [batch, num_vertices, 2] |
| 사용법 | `corrected_coords = input_coords + offsets` |
| 최대 정점 수 | 500 |

## 설치

### 요구 사항

- Python 3.10 이상
- CUDA 지원 GPU (선택사항, CPU도 가능)

### 의존성 설치

```bash
cd AI_Engine
pip install -r requirements.txt
```

`requirements.txt` 내용:
```
torch>=2.0.0
numpy>=1.24.0
shapely>=2.0.0
onnx>=1.14.0
onnxruntime>=1.15.0
```

## 훈련

### 기본 훈련 (합성 데이터)

```bash
cd AI_Engine
python training/ai_training_pipeline.py
```

100 에폭 훈련 후 `checkpoints/best_model.pt`에 모델 저장됨.

### FGDB 데이터로 추가 훈련

```bash
python train_with_fgdb.py
```

FGDB 파일을 자동으로 검색하여 Fine-tuning 수행.

### 훈련 설정 (DEFAULT_CONFIG)

```python
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
```

## ONNX 모델 내보내기

```bash
python export_model.py
```

출력:
- `models/geometry_corrector.onnx` - ONNX 모델 파일
- `models/model_metadata.json` - 모델 메타데이터

## 성능 테스트

```bash
python performance_test.py
```

### 성능 결과 (CPU, 2026-01-04)

| 항목 | 값 |
|------|-----|
| 단일 추론 | ~1ms |
| 처리량 | ~1,000 geometries/sec |
| 1,000 geometries | ~1초 |
| 10,000 geometries | ~10초 |
| 100,000 geometries | ~1.5분 |

## 파일 구조

```
AI_Engine/
├── training/
│   └── ai_training_pipeline.py   # 메인 훈련 파이프라인 (970줄)
├── checkpoints/
│   └── best_model.pt             # 최신 훈련된 모델
├── models/
│   ├── geometry_corrector.onnx   # ONNX 모델
│   └── model_metadata.json       # 메타데이터
├── export_model.py               # ONNX 내보내기 스크립트
├── train_with_fgdb.py            # FGDB Fine-tuning 스크립트
├── performance_test.py           # 성능 테스트 스크립트
├── requirements.txt              # Python 의존성
└── README.md                     # 이 문서
```

## 모델 아키텍처

### GeometryGNN

```
Input (coordinates, mask)
    │
    ▼
Linear (2 → 128)
    │
    ▼
GraphConvLayer × 3
├── 이웃 정점 집계 (prev, next)
├── BatchNorm
├── ReLU
├── Dropout
└── Residual Connection
    │
    ▼
Linear (128 → 2)
    │
    ▼
Output (offsets)
```

### 손실 함수

```python
GeometryLoss = MSE_Loss + α × Smoothness_Loss

# MSE: 예측 오프셋과 실제 오프셋 차이
# Smoothness: 인접 정점 간 오프셋 변화율 (기하학적 연속성)
```

## C# 연동

### appsettings.json

```json
{
  "AI": {
    "Enabled": true,
    "ModelPath": "Resources/Models/geometry_corrector.onnx",
    "FallbackToBuffer": true,
    "AreaTolerancePercent": 1.0
  }
}
```

### 사용 예시 (C#)

```csharp
using SpatialCheckProMax.Services.Ai;

var corrector = new GeometryAiCorrector("path/to/model.onnx", logger);
var corrected = corrector.Correct(inputGeometry);

if (corrected != inputGeometry)
{
    Console.WriteLine("지오메트리가 수정되었습니다.");
}
```

## 문제 해결

### ONNX 내보내기 오류

PyTorch 2.9+ 에서 ONNX 내보내기 시 onnxscript 필요:
```bash
pip install onnxscript
```

### QGIS Python 충돌

Windows에서 QGIS Python이 기본 Python과 충돌할 수 있음:
```bash
# 명시적 Python 경로 사용
"C:\Users\username\AppData\Local\Programs\Python\Python312\python.exe" training/ai_training_pipeline.py
```

### GPU 메모리 부족

배치 크기 줄이기:
```python
config["batch_size"] = 16  # 기본 32에서 줄임
```

## 라이선스

(주)우리강산시스템 - 내부 사용 전용
