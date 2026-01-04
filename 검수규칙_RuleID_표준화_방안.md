# ISO 19157 기반 검수규칙 RuleID 표준화 방안 및 영향도 분석

## 1. 개요
현재 SpatialCheckProMax의 검수 규칙 ID(`RuleId`)는 한글 명칭(예: `건물_도로경계면_침범`), 영문 약어(예: `ATTR_CODE_BULD`), 임의 코드(예: `G26`)가 혼재되어 있어 체계적인 관리와 확장이 어렵습니다.
이에 ISO 19157 지리정보 데이터 품질 표준을 기반으로 RuleID 체계를 정비하여 일관성을 확보하고 유지보수 효율성을 높이고자 합니다.

---

## 2. RuleID 표준화 체계 (제안)

### 2.1. 코드 구조
`[대분류]_[중분류]_[소분류]_[일련번호]` 형태의 4단 구조를 제안합니다.

| 세그먼트 | 길이 | 설명 | 예시 |
| :--- | :--- | :--- | :--- |
| **대분류** | 3자리 | ISO 19157 품질 요소 (Quality Element) | `COM`, `LOG`, `POS`, `THE` |
| **중분류** | 3자리 | ISO 19157 세부 요소 (Sub-element) | `CMS`, `FMT`, `TOP`, `DOM` |
| **소분류** | 3자리 | 검수 대상 객체/유형 약어 | `TBL`, `SCH`, `GEO`, `REL` |
| **일련번호** | 3자리 | 고유 식별 번호 | `001`, `002`, ... |

### 2.2. 분류 코드 정의

#### 대분류 (Quality Element)
*   **`COM`** (Completeness): 완전성 (데이터의 누락/초과)
*   **`LOG`** (Logical Consistency): 논리적 일관성 (구조, 포맷, 위상 등)
*   **`POS`** (Positional Accuracy): 위치 정확도 (좌표 정밀도)
*   **`THE`** (Thematic Accuracy): 주제 정확도 (속성값 정확도, 분류 등)
*   **`TMP`** (Temporal Accuracy): 시간 정확도 (현재 사용 안함)

#### 중분류 (Sub-element)
*   **완전성 (`COM`)**
    *   `OMS` (Omission): 누락 (데이터가 빠짐)
    *   `CMS` (Commission): 초과 (불필요한 데이터 존재)
*   **논리적 일관성 (`LOG`)**
    *   `FMT` (Format Consistency): 포맷 일관성 (스키마, 파일형식)
    *   `DOM` (Domain Consistency): 도메인 일관성 (속성값 범위)
    *   `TOP` (Topological Consistency): 위상 일관성 (교차, 중첩, 연결성)
    *   `CNC` (Conceptual Consistency): 개념 일관성 (스키마 구조)
*   **주제 정확도 (`THE`)**
    *   `CLS` (Classification Correctness): 분류 정확도
    *   `ATT` (Non-quantitative Attribute Accuracy): 정성적 속성 정확도
    *   `QNT` (Quantitative Attribute Accuracy): 정량적 속성 정확도

#### 소분류 (Target Type)
*   `TBL`: 테이블/파일 레벨
*   `SCH`: 스키마/컬럼 레벨
*   `GEO`: 단독 지오메트리
*   `REL`: 레이어 간 관계
*   `ATR`: 속성값

### 2.3. 적용 예시 (New RuleID)

| 단계 | 기존 RuleId | 표준 RuleId (New) | 설명 | ISO 요소 |
| :--- | :--- | :--- | :--- | :--- |
| 1단계 | (없음) | `COM_OMS_TBL_001` | 필수 테이블 누락(데이터 없음) | 완전성-누락 |
| 2단계 | `SCH_COL_MISSING` | `LOG_CNC_SCH_001` | 필수 컬럼(구조) 누락 | 논리-개념 |
| 2단계 | `SCH_VAL_NULL` | `COM_OMS_ATR_001` | 필수 속성값(Value) 누락 | 완전성-누락 |
| 2단계 | `SCH_FK_VIOLATION` | `LOG_CNC_SCH_002` | 외래키 참조 위반 | 논리-개념 |
| 3단계 | `GEOM_SELF_INTERSECTION` | `LOG_TOP_GEO_001` | 지오메트리 자체 교차 | 논리-위상 |
| 3단계 | `GEOM_SHORT_LINE` | `LOG_TOP_GEO_002` | 너무 짧은 객체 | 논리-위상 |
| 4단계 | `ATTR_CODE_BULD` | `THE_CLS_ATR_001` | 건물 용도 코드 오류 | 주제-분류 |
| 4단계 | `ATTR_NOTZERO_FLOOR` | `LOG_DOM_ATR_001` | 층수 0 금지 (도메인) | 논리-도메인 |
| 5단계 | `건물_도로경계면_침범` | `LOG_TOP_REL_001` | 건물-도로 겹침 | 논리-위상 |
| 5단계 | `도로중심선_미연결` | `LOG_TOP_REL_002` | 중심선 미연결 | 논리-위상 |

---

## 3. 개발 영향도 분석

RuleID 체계를 변경하는 것은 단순 문자열 교체가 아니라 시스템 전반에 영향을 미치는 작업입니다.

### 3.1. 설정 파일 (`SpatialCheckProMax/Config/*.csv`) [영향도: 높음]
*   **대상**: `1_table_check.csv` ~ `5_relation_check.csv` 등 모든 설정 파일.
*   **작업**: `RuleId` 컬럼의 값을 새로운 표준 ID로 전면 교체해야 함.
*   **리스크**: 기존에 사용하던 RuleId와 매핑이 끊어지면 과거 이력 관리가 어려울 수 있음. (별도 매핑 테이블 필요)

### 3.2. 소스 코드 (`Source Code`) [영향도: 중간]
*   **하드코딩된 RuleId**: 코드 내에서 특정 RuleId를 `switch`문이나 `if`문으로 분기 처리하는 로직이 있다면 수정 필요.
    *   예: `GeometryCheckProcessor`에서 `GEOM_SHORT_LINE` 등을 직접 참조하는 경우.
    *   예: `QcErrorDataService`에서 특정 에러 코드에 따라 색상을 지정하는 경우.
*   **검색/필터 로직**: `RuleId`의 패턴(Prefix 등)을 사용하는 로직 점검 필요. (예: `#` 주석 처리 로직은 영향 없음)

### 3.3. 데이터베이스/결과 파일 (`Output`) [영향도: 높음]
*   **기존 결과 호환성**: 이미 생성된 `QC_ERRORS` GDB 파일이나 리포트의 RuleId와 새로운 RuleId가 섞이면 통계 산출 시 혼란 발생.
*   **대책**: RuleID 변경 시점(버전)을 명확히 하고, 기존 데이터 마이그레이션은 지원하지 않거나(새로 검수 권장), 별도 매핑 도구 제공.

### 3.4. UI/UX [영향도: 낮음]
*   화면에 표시되는 RuleID가 길어지고 영문 코드로 변경되므로, 사용자 친화적인 **규칙명(Rule Name/Description)**을 별도 컬럼으로 관리하여 UI에는 한글명을 우선 표시하도록 수정 필요.

---

## 4. 이행 로드맵

### Phase 1: 매핑 정의 (1주차)
1.  기존 1~5단계의 모든 검수 항목 목록화.
2.  ISO 19157 요소에 따라 분류 및 신규 RuleID 부여.
3.  `Old_RuleID` <-> `New_RuleID` 매핑 테이블(CSV) 작성.

### Phase 2: 설정 파일 마이그레이션 (2주차)
1.  Config 폴더 내 CSV 파일들의 `RuleId` 일괄 변경.
2.  동시에 `Description` 또는 `RuleName` 컬럼을 추가하여 기존의 한글 명칭 보존.

### Phase 3: 코드 리팩토링 (2주차)
1.  하드코딩된 RuleID 참조 상수(`Constants/CheckIds.cs` 등) 수정.
2.  `QcError` 생성 시 신규 ID 체계 적용 확인.
3.  UI에서 RuleID 대신 설명(Description)을 보여주도록 바인딩 수정.

### Phase 4: 검증 (3주차)
1.  전체 빌드 및 단위 테스트 수행.
2.  샘플 데이터 검수 실행 후 결과 GDB의 `ErrCode`/`RuleId` 필드 확인.


