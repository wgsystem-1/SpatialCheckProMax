# CLAUDE.md - SpatialCheckProMax

## 프로젝트 개요

**SpatialCheckProMax**는 File Geodatabase(FGDB)의 공간 데이터 품질을 검증하는 Windows 데스크톱 애플리케이션입니다. 국가기본도 데이터 검증을 위해 설계되었으며, 6단계 자동화 검증 파이프라인을 구현합니다.

- **회사**: (주)우리강산시스템 (WGSYSTEM)
- **언어**: C# 12.0
- **프레임워크**: .NET 9.0, WPF, Entity Framework Core
- **대상 OS**: Windows 10/11 (64비트)

## 솔루션 구조

```
SpatialCheckProMax.sln
├── SpatialCheckProMax/           # 핵심 비즈니스 로직 라이브러리 (net9.0)
│   ├── Config/                   # CSV 검증 규칙 설정 파일
│   ├── Processors/               # 검증 로직 (테이블, 스키마, 지오메트리, 속성, 관계)
│   ├── Services/                 # 105개 이상의 서비스 클래스
│   ├── Models/                   # 도메인 모델 및 DTO
│   └── Data/                     # EF Core DbContext
├── SpatialCheckProMax.GUI/       # WPF 데스크톱 애플리케이션 (net9.0-windows)
│   ├── Views/                    # XAML 윈도우 및 사용자 컨트롤
│   ├── ViewModels/               # MVVM ViewModel(뷰모델)
│   └── Converters/               # XAML 값 변환기
├── SpatialCheckProMax.Api/       # REST API 서버 (ASP.NET Core)
└── SpatialCheckProMax.Tests/     # xUnit 테스트
```

## 빌드 명령어

```powershell
# 솔루션 빌드
dotnet build -c Release

# GUI 애플리케이션 실행
dotnet run --project SpatialCheckProMax.GUI

# API 서버 실행
dotnet run --project SpatialCheckProMax.Api

# 테스트 실행
dotnet test SpatialCheckProMax.Tests

# Self-contained(자체 포함) 배포 빌드
dotnet publish -c Release -r win-x64 --self-contained

# 클린
dotnet clean
```

## 검증 파이프라인 (6단계)

1. **Table Check(테이블 검사)** - 테이블 존재 여부, 좌표계, Geometry(지오메트리) 타입 확인
2. **Schema Check(스키마 검사)** - 컬럼 구조, 데이터 타입, 제약조건 검증
3. **Geometry Check(지오메트리 검사)** - 공간 오류 탐지 (중복, 중첩, 꼬임, Sliver(슬리버) 등)
4. **Attribute Check(속성 검사)** - Codelist(코드리스트) 기반 속성값 검증
5. **Relation Check(관계 검사)** - 레이어 간 공간 관계 검증
6. **Report Generation(보고서 생성)** - PDF/HTML 검증 보고서 생성

## 주요 기술 스택

- **GDAL** (MaxRev.Gdal.Core): FGDB 읽기/쓰기
- **NetTopologySuite**: Geometry(지오메트리) 연산 및 Topology(토폴로지) 분석
- **Entity Framework Core + SQLite**: 결과 데이터 저장
- **Serilog**: 구조화된 로깅
- **iTextSharp/PdfSharp**: PDF 보고서 생성
- **CommunityToolkit.MVVM**: MVVM 패턴 구현

## 설정 파일

| 파일 | 설명 |
|------|------|
| `SpatialCheckProMax/appsettings.json` | 핵심 설정 (로깅, DB, 파일 제한 등) |
| `Config/1_table_check.csv` | 테이블 검증 규칙 |
| `Config/2_schema_check.csv` | 스키마 검증 규칙 |
| `Config/3_geometry_check.csv` | Geometry(지오메트리) 오류 탐지 규칙 |
| `Config/4_attribute_check.csv` | 속성 검증 규칙 |
| `Config/5_relation_check.csv` | 레이어 관계 검증 규칙 |
| `Config/codelist.csv` | 속성값 유효 코드 목록 |

## 코드 컨벤션

### 네이밍 규칙
- **Class(클래스)/Method(메서드)/Property(속성)**: PascalCase
- **Private Field(비공개 필드)**: `_camelCase` (언더스코어 접두사)
- **Constant(상수)**: UPPER_SNAKE_CASE
- **Interface(인터페이스)**: `I` 접두사 (예: `IRelationCheckProcessor`)

### 패턴
- **Async(비동기) 메서드**: `Async` 접미사 사용, 항상 `CancellationToken` 전달
- **Dependency Injection(의존성 주입)**: 생성자 주입 방식 사용
- **MVVM**: CommunityToolkit.MVVM Attribute(어트리뷰트) 활용
- **Strategy Pattern(전략 패턴)**: `RelationCheckProcessor`의 공간 검사에 적용

### Error Handling(오류 처리)
- Custom Exception(사용자 정의 예외)은 `Exceptions/` 폴더에 위치
- Serilog를 통한 구조화된 로깅
- 검증 오류는 `QC_ERRORS` 테이블에 저장

## 주요 파일

| 파일 | 설명 |
|------|------|
| `Processors/RelationCheckProcessor.cs` | 공간 관계 검증 (5300줄 이상, 대형 클래스) |
| `Processors/GeometryCheckProcessor.cs` | Geometry(지오메트리) 오류 탐지 |
| `Services/GdalDataReader.cs` | GDAL 연동 레이어 |
| `Services/CsvConfigService.cs` | CSV 검증 규칙 파싱 |
| `GUI/ViewModels/MainViewModel.cs` | 메인 애플리케이션 상태 관리 |

## 문서

- `docs/01_개발스택_문서.md` - 개발 스택 상세
- `docs/02_시스템_매뉴얼.md` - 시스템 매뉴얼
- `docs/03_사용법_가이드.md` - 사용자 가이드
- `docs/04_아키텍처_설계서.md` - 아키텍처 설계 명세

## 알려진 이슈

- 대형 Processor(프로세서) 클래스 분해 필요 (`RelationCheckProcessor`, `GeometryCheckProcessor`)
- 테스트 커버리지 부족
- `MainWindow.xaml.cs`의 일부 비즈니스 로직을 서비스로 이동 필요

## 성능 관련 참고사항

- 1.8GB 초과 파일은 Streaming Mode(스트리밍 모드) 사용
- Custom Spatial Index(사용자 정의 공간 인덱스): `GridSpatialIndex`, `RTreeSpatialIndex`
- Batch Size(배치 크기) 및 Parallel Processing(병렬 처리) 설정 가능
- `AdvancedMemoryManager`를 통한 메모리 관리
