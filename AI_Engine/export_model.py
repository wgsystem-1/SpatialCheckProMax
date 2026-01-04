"""ONNX 모델 내보내기 스크립트"""
import sys
import os

# 경로 추가
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.chdir(os.path.dirname(os.path.abspath(__file__)))

from training.ai_training_pipeline import export_for_csharp, DEFAULT_CONFIG

export_for_csharp(
    'checkpoints/best_model.pt',
    output_dir='models',
    config=DEFAULT_CONFIG
)
