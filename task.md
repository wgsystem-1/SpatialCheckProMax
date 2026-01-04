# task.md - SpatialCheckProMax 개선 로드맵

> 최종 업데이트: 2026-01-04 (P0/P1/P2/P3 완료 + AI GDB 저장 기능 구현)

## 우선순위 기준

| 등급 | 설명 |
|------|------|
| 🔴 **P0** | 즉시 조치 필요 (안정성/품질에 직접적 영향) |
| 🟠 **P1** | 단기 개선 (1~2주 내 권장) |
| 🟡 **P2** | 중기 개선 (1개월 내 권장) |
| 🟢 **P3** | 장기 개선 (분기 내 권장) |

---

## 🔴 P0: 즉시 조치 필요 ✅ 완료

### 1. 테스트 커버리지 확보 ✅
**현황**: ~~유닛 테스트 0개~~ → **280개 테스트 작성 완료**

**완료 항목**:
- [x] `SpatialCheckProMax.Tests` 프로젝트 활성화 및 기본 구조 설정
- [x] 핵심 Processor(프로세서) 테스트 작성
  - [x] `TableCheckProcessor` 테스트 (10개)
  - [x] `TableCheckConfig` 모델 테스트 (13개)
  - [x] `RelationCheckProcessor` 테스트 (8개)
  - [x] `RelationChecks` Strategy(전략) 테스트 (25개+)
  - [x] `SchemaCheckProcessor` 테스트 (45개+) (**신규**)
  - [x] `GeometryCheckProcessor` 테스트 (55개+) (**신규**)
  - [x] `AttributeCheckProcessor` 테스트 (35개+) (**신규**)
- [x] `CsvConfigService` 테스트 (15개+)
- [x] `ValidationOrchestrator` 관련 모델 테스트 (17개)

**관련 파일**:
- `SpatialCheckProMax.Tests/Processors/TableCheckProcessorTests.cs`
- `SpatialCheckProMax.Tests/Processors/RelationCheckProcessorTests.cs`
- `SpatialCheckProMax.Tests/Processors/SchemaCheckProcessorTests.cs` (**신규**)
- `SpatialCheckProMax.Tests/Processors/GeometryCheckProcessorTests.cs` (**신규**)
- `SpatialCheckProMax.Tests/Processors/AttributeCheckProcessorTests.cs` (**신규**)
- `SpatialCheckProMax.Tests/Models/Config/TableCheckConfigTests.cs`
- `SpatialCheckProMax.Tests/Services/CsvConfigServiceTests.cs`

---

### 2. 불필요한 파일 정리 ✅
**현황**: 정리 완료

**완료 항목**:
- [x] `SpatialCheckProMax.GUI/App_New.xaml.cs` - 이미 삭제됨
- [x] 중복 공간 인덱스 클래스 검토 완료
  - `GridSpatialIndex`, `QuadTreeSpatialIndex`, `RTreeSpatialIndex`, `OptimizedRTreeSpatialIndex`
  - **결론**: 모두 `SpatialIndexManager`를 통해 활용 중. DI에 등록되어 동적으로 선택 가능.
  - 삭제 불필요 - 성능 최적화를 위한 다양한 인덱스 전략 제공

---

## 🟠 P1: 단기 개선

### 3. God Class(거대 클래스) 분해

#### 3-1. RelationCheckProcessor 리팩토링 ✅ 완료
**현황**: 271KB → Strategy 패턴 적용으로 27개 개별 클래스로 분리 완료

**완료 항목**:
- [x] Strategy Pattern(전략 패턴) 적용
  - [x] `IRelationCheckStrategy` 인터페이스 정의
  - [x] `BaseRelationCheckStrategy` 기반 클래스 구현 (공통 헬퍼 메서드 포함)
  - [x] 개별 Strategy(전략) 클래스 분리 (27개 완료):
    - [x] `PointInsidePolygonStrategy` - 점이 폴리곤 내부에 있는지 검사
    - [x] `LineWithinPolygonStrategy` - 선이 폴리곤 내부에 있는지 검사
    - [x] `PolygonBoundaryMatchStrategy` - 폴리곤 경계 일치 검사
    - [x] `BuildingCenterPointsStrategy` - 건물중심점 검사
    - [x] `SharpBendCheckStrategy` - 등고선/도로 꺾임 검사 (ContourSharpBend + RoadSharpBend 통합)
    - [x] `ContourIntersectionStrategy` - 등고선 교차 검사
    - [x] `PolygonNotContainPointStrategy` - 폴리곤 내 점 포함 금지 검사
    - [x] `PolygonMissingLineStrategy` - 폴리곤 내 선형 누락 검사
    - [x] `PolygonNoOverlapStrategy` - 폴리곤 겹침 금지 검사
    - [x] `PolygonNotIntersectLineStrategy` - 폴리곤-선형 교차 금지 검사
    - [x] `LineConnectivityStrategy` - 선 연결성 검사
    - [x] `PolygonWithinPolygonStrategy` - 폴리곤 포함 관계 검사
    - [x] `PolygonContainsLineStrategy` - 폴리곤 내 선형 포함 검사
    - [x] `LineEndpointWithinPolygonStrategy` - 선형 끝점 폴리곤 포함 검사
    - [x] `ConnectedLinesSameAttributeStrategy` - 연결된 선분 속성값 일치 검사
    - [x] `LineDisconnectionStrategy` - 도로중심선 단절 검사
    - [x] `LineDisconnectionWithAttributeStrategy` - 속성별 도로경계선 단절 검사
    - [x] `DefectiveConnectionStrategy` - 결함있는 연결 검사
    - [x] `LineIntersectionWithAttributeStrategy` - 선형 객체 간 교차 검사
    - [x] `PolygonIntersectionWithAttributeStrategy` - 폴리곤 객체 간 교차 검사
    - [x] `PolygonNotWithinPolygonStrategy` - 폴리곤 비포함 검사
    - [x] `CenterlineAttributeMismatchStrategy` - 중심선 속성 불일치 검사 (하이브리드 방식)
    - [x] `BridgeRiverNameMatchStrategy` - 교량-하천 이름 일치 검사
    - [x] `PolygonContainsObjectsStrategy` - 경지경계 내부 객체 포함 검사
    - [x] `HoleDuplicateCheckStrategy` - 홀 중복 객체 검사
    - [x] `AttributeSpatialMismatchStrategy` - 속성-공간 불일치 검사
    - [x] `PointSpacingCheckStrategy` - 표고점 위치 간격 검사
- [x] `RelationCheckProcessor`가 Strategy(전략) 디스패처로 동작
- [x] `BaseRelationCheckStrategy`에 공통 헬퍼 메서드 통합:
  - `AddEndpointToIndex`, `SearchEndpointsNearby` - 끝점 인덱싱
  - `CalculateAngleDifference` - 벡터 각도 계산
  - `ParseSqlStyleFilter`, `ShouldIncludeByFilter` - SQL 필터 파싱
  - `BuildUnionGeometry` - 지오메트리 Union
  - `GetSurfaceArea`, `GetFieldIndexIgnoreCase`, `GetFieldValueSafe` 등

**관련 파일**:
- `SpatialCheckProMax/Processors/RelationCheckProcessor.cs`
- `SpatialCheckProMax/Processors/RelationChecks/` (27개 Strategy 파일)

#### 3-2. GeometryCheckProcessor 리팩토링 ✅ 완료
**현황**: 106KB → Strategy 패턴 적용으로 9개 개별 클래스로 분리 완료

**완료 항목**:
- [x] Strategy Pattern(전략 패턴) 적용
  - [x] `IGeometryCheckStrategy` 인터페이스 정의
  - [x] `BaseGeometryCheckStrategy` 기반 클래스 구현 (공통 헬퍼 메서드 포함)
  - [x] `GeometryCheckContext` 컨텍스트 클래스 정의
  - [x] 개별 Strategy(전략) 클래스 분리 (9개 완료):
    - [x] `GeosValidityCheckStrategy` - GEOS 유효성/자기교차 검사
    - [x] `ShortObjectCheckStrategy` - 짧은 선형 객체 검사
    - [x] `SmallAreaCheckStrategy` - 작은 면적 폴리곤 검사
    - [x] `MinPointsCheckStrategy` - 최소 정점 수 검사
    - [x] `SliverCheckStrategy` - 슬리버 폴리곤 검사
    - [x] `SpikeCheckStrategy` - 스파이크 검사
    - [x] `DuplicateCheckStrategy` - 중복 지오메트리 검사
    - [x] `OverlapCheckStrategy` - 겹침 지오메트리 검사
    - [x] `UndershootOvershootCheckStrategy` - 언더슛/오버슛 검사
- [x] `GeometryCheckProcessorRefactored` Strategy 디스패처 구현
- [x] 38개 Strategy 테스트 작성 완료

**관련 파일**:
- `SpatialCheckProMax/Processors/GeometryCheckProcessor.cs` (기존 - 하위호환)
- `SpatialCheckProMax/Processors/GeometryCheckProcessorRefactored.cs` (신규 - Strategy 패턴)
- `SpatialCheckProMax/Processors/GeometryChecks/` (9개 Strategy 파일)
- `SpatialCheckProMax.Tests/Processors/GeometryChecks/GeometryCheckStrategyTests.cs`

#### 3-3. MainWindow.xaml.cs 정리 ✅ 완료
**현황**: ValidationOrchestrator 연동 완료. MainWindow가 Orchestrator에 위임하도록 리팩토링됨.

**완료 항목**:
- [x] `IValidationOrchestrator` 인터페이스 정의
- [x] `ValidationOrchestrator` 서비스 구현 (단일/배치 검수 오케스트레이션)
- [x] `ValidationOrchestratorOptions` 옵션 클래스 정의
- [x] `FileCompletedEventArgs`, `ValidationCompletedEventArgs` 이벤트 인자 정의
- [x] DI 등록 (`DependencyInjectionConfigurator`)
- [x] 관련 모델 테스트 17개 작성
- [x] `MainWindow.xaml.cs`에서 `ValidationOrchestrator` 사용하도록 리팩토링
  - `_validationService` → `_validationOrchestrator`로 변경
  - `StartValidationAsync`, `StartBatchValidationAsync` 로직 간소화
  - 이벤트 핸들러 연결 (`ProgressUpdated`, `FileCompleted`, `ValidationCompleted`)
  - `CreateValidationOptions()` 헬퍼 메서드 추가 (타입 변환)
- [x] 불필요한 폴백 기본값 제거 (`tableCount`, `featureCount` 폴백 - 예측 시스템이 0 값 처리 가능)
- [x] Event Handler(이벤트 핸들러)는 최소한의 위임 코드만 유지

**신규 파일**:
- `SpatialCheckProMax.GUI/Services/IValidationOrchestrator.cs`
- `SpatialCheckProMax.GUI/Services/ValidationOrchestrator.cs`
- `SpatialCheckProMax.Tests/Services/ValidationOrchestratorTests.cs`

**관련 파일**:
- `SpatialCheckProMax.GUI/MainWindow.xaml.cs`
- `SpatialCheckProMax.GUI/ViewModels/MainViewModel.cs`

---

## 🟡 P2: AI 자동 수정 기능 완성 ✅

### 5. AI 모델 생성 및 훈련 ✅ 완료

**현황**:
- ✅ `GeometryAiCorrector.cs` - ONNX 런타임 통합 코드 완성 (마스크 + 오프셋 방식)
- ✅ `GeometryAiValidator.cs` - 검증 로직 구현 완료
- ✅ `ai_training_pipeline.py` - 완전한 GNN 훈련 파이프라인 구현 완료
- ✅ `requirements.txt` - Python 의존성 정의

**모델 상세**:
- **모델명**: `GeometryGNN` (Graph Neural Network)
- **프레임워크**: PyTorch → ONNX (로컬 실행, 외부 API 불필요)
- **입력**: `coordinates [batch, num_vertices, 2]`, `mask [batch, num_vertices]`
- **출력**: `offsets [batch, num_vertices, 2]` (보정 오프셋 dx, dy)
- **사용법**: `corrected_coords = input_coords + offsets`

**완료 항목**:

#### 5-1. 훈련 데이터 생성 ✅
- [x] `ai_training_pipeline.py` 완성
  - [x] 노이즈 주입 함수 (`inject_vertex_noise`) - 정점별 랜덤 노이즈
  - [x] 위상 오류 생성 함수 (`create_topology_errors`) - Gap, Overlap, Spike, Shift
  - [x] 합성 지오메트리 생성 (`generate_synthetic_polygon`, `generate_synthetic_line`)
  - [x] `GeometryDataset` - 합성 데이터셋 클래스
  - [x] `FGDBGeometryDataset` - FGDB 로드 데이터셋 클래스 (GDAL 연동)

#### 5-2. GNN 모델 훈련 ✅
- [x] `GraphConvLayer` - 이웃 정점 집계 그래프 컨볼루션
- [x] `GeometryGNN` - 3레이어 GNN (128 hidden dim, BatchNorm, Residual)
- [x] `GeometryLoss` - MSE + Smoothness 복합 손실 함수
- [x] `Trainer` - AdamW + CosineAnnealing 스케줄러

#### 5-3. ONNX 모델 내보내기 ✅
- [x] `export_to_onnx()` - 동적 축 지원 ONNX 내보내기
- [x] `export_for_csharp()` - 메타데이터 포함 패키지 생성
- [x] C# `GeometryAiCorrector` - 새 모델 형식 연동 완료

**실행 방법**:
```bash
cd AI_Engine
pip install -r requirements.txt
python training/ai_training_pipeline.py
```

**관련 파일**:
- `AI_Engine/training/ai_training_pipeline.py` (970줄 완전 구현)
- `AI_Engine/requirements.txt`
- `SpatialCheckProMax/Services/Ai/GeometryAiCorrector.cs`

---

### 6. AI-GUI 통합 ✅ 완료

**현황**:
- ✅ `IGeometryEditToolService` 인터페이스 정의됨
- ✅ `GeometryEditToolService.AutoFixGeometryAsync()` 구현됨 (AI 우선 + Buffer(0) 폴백)
- ✅ AI 서비스 DI 등록 완료
- ✅ appsettings.json AI 설정 추가 완료
- ✅ GUI "AI 자동 수정" 버튼 추가 완료
- ✅ GDB 파일 저장 기능 구현 완료

**완료 항목**:

#### 6-1. AI 서비스 통합 ✅
- [x] `appsettings.json`에 AI 설정 추가
  ```json
  {
    "AI": {
      "Enabled": true,
      "ModelPath": "Resources/Models/geometry_corrector.onnx",
      "FallbackToBuffer": true,
      "AreaTolerancePercent": 5.0,
      "MaxVertices": 1024
    }
  }
  ```
- [x] `AppSettings.cs`에 `AISettings` 클래스 추가
- [x] `DependencyInjectionConfigurator`에 AI 서비스 등록
  - `GeometryAiCorrector` - 싱글톤 (ONNX 모델 없으면 null 반환)
  - `GeometryAiValidator` - 싱글톤
  - `IGeometryEditToolService` - 싱글톤
  - `IGdalGeometryWriter` - 싱글톤 (**신규**)

#### 6-2. AutoFix 로직 개선 ✅
- [x] `GeometryEditToolService.AutoFixGeometryAsync()` 수정
  - AI 모델 수정 시도 (모델 있을 경우)
  - AI 검증기로 결과 검증
  - 실패 시 Buffer(0) 전략으로 폴백
  - `forceApply` 파라미터 추가 - 검수 오류는 NTS IsValid와 별개이므로 강제 수정 적용
- [x] 수정 이력 로깅 추가

#### 6-3. 오류 처리 및 Fallback(폴백) ✅
- [x] AI 모델 로드 실패 시 graceful fallback(우아한 폴백) - null 반환으로 처리
- [x] 추론 실패 시 Buffer(0) 전략으로 대체
- [x] 로그에 수정 방법 표시 (AI vs Buffer)

#### 6-4. GUI "AI 자동 수정" 버튼 ✅ (**신규**)
- [x] `ValidationResultView.xaml`에 "AI 자동 수정" 버튼 추가 (3단계 지오메트리 검수 탭)
- [x] `AiAutoFixButton_Click` 이벤트 핸들러 구현
  - 오류 목록에서 지오메트리 추출 (WKT 파싱)
  - AI 수정 호출 (`forceApply: true`)
  - GDB 파일에 저장
  - 결과 메시지 표시 (성공/실패 건수)

#### 6-5. GDB 파일 저장 기능 ✅ (**신규**)
- [x] `IGdalGeometryWriter` 인터페이스 정의
- [x] `GdalGeometryWriter` 구현
  - `UpdateGeometryAsync()` - 단일 피처 업데이트
  - `UpdateGeometriesBatchAsync()` - 일괄 피처 업데이트
  - OpenFileGDB 드라이버 사용 (GDAL 3.6+ 쓰기 지원)
  - **Delete + Create 전략**: SetFeature 미지원 시 자동 폴백
    - 레이어 기능 자동 감지 (SetFeature, DeleteFeature, CreateFeature)
    - 원본 피처 속성 복사 → 삭제 → 새 피처 생성
  - NTS Geometry → OGR Geometry 변환 (WKT 경유)
- [x] DI 등록 완료

**관련 파일**:
- `SpatialCheckProMax.GUI/Views/ValidationResultView.xaml` (**신규 버튼**)
- `SpatialCheckProMax.GUI/Views/ValidationResultView.xaml.cs` (**신규 핸들러**)
- `SpatialCheckProMax.GUI/Services/GeometryEditToolService.cs`
- `SpatialCheckProMax.GUI/Services/IGeometryEditToolService.cs`
- `SpatialCheckProMax.GUI/Services/DependencyInjectionConfigurator.cs`
- `SpatialCheckProMax/Services/IO/GdalGeometryWriter.cs` (**신규**)
- `SpatialCheckProMax/Services/Ai/GeometryAiCorrector.cs`
- `SpatialCheckProMax/Services/Ai/GeometryAiValidator.cs`
- `SpatialCheckProMax.GUI/appsettings.json`

---

### 7. AI 수정 테스트 및 검증 ✅ 완료

**완료 항목**:
- [x] AI 수정 유닛 테스트 작성 (29개 테스트 추가, 총 309개)
  - [x] `GeometryAiCorrectorTests.cs` - 16개 테스트
    - 생성자 테스트 (null/empty/nonexistent path)
    - Correct 메서드 테스트 (null, empty, model not loaded, too many vertices)
    - CorrectBatch 테스트
    - GetCorrectionConfidence 테스트 (null, different vertex count, identical, offset)
    - Dispose 테스트
  - [x] `GeometryAiValidatorTests.cs` - 13개 테스트
    - Validate 메서드 테스트 (null, invalid, valid, area change)
    - Point/LineString 지오메트리 테스트
    - Edge case 테스트 (empty, zero area)
- [x] 성능 테스트 완료
  - 단일 추론: ~1ms
  - 처리량: ~1,000 geometries/sec
  - 100,000 geometries: ~1.5분

**관련 파일**:
- `SpatialCheckProMax.Tests/Services/Ai/GeometryAiCorrectorTests.cs`
- `SpatialCheckProMax.Tests/Services/Ai/GeometryAiValidatorTests.cs`
- `AI_Engine/performance_test.py`
- `AI_Engine/performance_results.json`

---

## 🟢 P3: 장기 개선

### 8. 서비스 구조화 ✅ 완료
**현황**: `Services/` 디렉토리 도메인별 구조화 완료 (11개 폴더)

**완료된 구조**:
```
Services/
├── Ai/           # AI 보정 서비스 (GeometryAiCorrector, GeometryAiValidator)
├── Cache/        # 캐싱 (DataCacheService, LruCache)
├── Config/       # 설정 (CsvConfigService, AppSettingsService, LoggingService)
├── Geometry/     # 공간 인덱스 및 지오메트리 (19개 파일)
├── Interfaces/   # 공통 인터페이스
├── IO/           # 파일 I/O (GdalDataReader, Streaming, File)
├── Memory/       # 메모리 관리 (AdvancedMemoryManager)
├── Parallel/     # 병렬 처리 (ProcessingManager, BatchSize)
├── QcError/      # QC 오류 관리 서비스
├── RemainingTime/# 남은 시간 계산
├── Reporting/    # 보고서 생성 (PDF, HTML, Excel)
├── Security/     # 보안 서비스 (FileSecurityService)
└── Validation/   # 검증 서비스 (ValidationService, Validators)
```

**결과**:
- 빌드: 성공 (경고 0, 오류 0)
- 테스트: 309개 모두 통과

---

### 9. CI/CD 파이프라인 구축 ✅ 완료
- [x] GitHub Actions 설정
  - `.github/workflows/ci.yml` - 메인 CI/CD 파이프라인
  - `.github/workflows/build.yml` - 빌드 전용 워크플로우
  - `.github/dependabot.yml` - 의존성 자동 업데이트
- [x] 자동 빌드 및 테스트 (windows-latest, .NET 9.0)
- [x] 코드 커버리지 리포트 (Codecov 연동)
- [x] 자동 릴리스 (태그 기반 v* 트리거)
- [x] GUI/API 아티팩트 생성 (Self-contained)

### 10. 성능 최적화 검증
- [ ] 커스텀 공간 인덱스 vs NetTopologySuite 인덱서 벤치마크
- [ ] 불필요한 인덱스 구현체 제거
- [ ] 메모리 프로파일링 및 최적화

### 11. 문서화 개선
- [ ] API 문서 자동 생성 (DocFX 또는 Sandcastle)
- [ ] 개발자 가이드 작성
- [x] AI 모델 훈련 가이드 작성 ✅ (`AI_Engine/README.md`)

---

## 진행 상태 요약

| 영역 | 완료 | 진행중 | 미시작 |
|------|------|--------|--------|
| 테스트 커버리지 | **309개** ✅ | - | - |
| 파일 정리 | ✅ | - | - |
| God Class 분해 | **100%** ✅ | - | - |
| AI 모델 훈련 파이프라인 | **100%** ✅ | - | - |
| AI-GUI 통합 | **100%** ✅ | - | - |
| AI 수정 테스트 | **100%** ✅ | - | - |
| 서비스 구조화 (P3) | **100%** ✅ | - | - |
| CI/CD (P3) | **100%** ✅ | - | - |

---

## 의존성 그래프

```
[P0] 테스트 커버리지 확보 ✅ (309개 테스트)
         │
         ▼
[P1] God Class 분해 ✅ (36개 전략 + ValidationOrchestrator + MainWindow 연동)
         │
         ▼
[P2] AI 모델 생성 ─────► [P2] AI-GUI 통합 ✅
         │                      │
         ▼                      ▼
[P2] AI 수정 테스트 ✅ ◄───────┘
         │
         ▼
[P3] 서비스 구조화 ✅ / CI/CD 파이프라인 ✅
```

> **참고**: P0(테스트) 완료! P1(리팩토링) 완료! **P2 완료!** **P3 완료!**
> 모든 우선순위 작업 완료!
>
> **최근 업데이트 (2026-01-04)**:
> - **P2 AI GUI 자동 수정 + GDB 저장 기능 완성**:
>   - `ValidationResultView.xaml` - "AI 자동 수정" 버튼 추가
>   - `ValidationResultView.xaml.cs` - 버튼 클릭 핸들러 (AI 수정 + GDB 저장)
>   - `GdalGeometryWriter.cs` - FileGDB 쓰기 서비스 구현
>     - OpenFileGDB 드라이버 사용
>     - **Delete + Create 전략**: SetFeature 미지원 시 자동 폴백
>     - 레이어 기능 자동 감지 (SetFeature, DeleteFeature, CreateFeature)
>   - `forceApply` 파라미터 추가 - 검수 오류는 NTS IsValid와 별개이므로 강제 수정 적용
>   - ONNX 모델 파일 자동 복사 (.csproj 설정)
>
> **이전 업데이트**:
> - P2 AI 훈련 파이프라인 완전 구현 완료
> - P3 CI/CD 파이프라인 구축 완료
> - 추가 테스트 90개 작성 (190개 → 280개 → 309개)
> - P1 완료: MainWindow.xaml.cs에서 ValidationOrchestrator 연동 완료
> - P2 AI-GUI 통합 완료
