# SpatialCheckProMax 검수 설정 가이드

이 디렉토리는 SpatialCheckProMax의 검수 규칙을 정의하는 설정 파일(`csv`)들을 포함하고 있습니다.
각 파일은 검수 단계(1~5단계)별 규칙과 기준 정보를 담고 있으며, 프로그램 실행 시 이 파일들을 로드하여 동적으로 검수를 수행합니다.

## 설정 파일 목록

| 파일명 | 단계 | 설명 |
| :--- | :---: | :--- |
| **`1_table_check.csv`** | 1 | **테이블 검수**: 필수 레이어 존재 여부, 지오메트리 타입, 좌표계(CRS) 정의 |
| **`2_schema_check.csv`** | 2 | **스키마 검수**: 필드 구조, 데이터 타입, 길이, 제약조건(PK, FK, UK, Not Null) 정의 |
| **`3_geometry_check.csv`** | 3 | **지오메트리 검수**: 객체 단위의 공간적 오류(중복, 꼬임, 미세 객체 등) 검사 활성화 여부 |
| **`geometry_criteria.csv`** | 3+ | **지오메트리 기준값**: 지오메트리 검수 시 사용되는 임계값(Tolerance) 및 기준값 정의 |
| **`4_attribute_check.csv`** | 4 | **속성 검수**: 속성값의 유효성, 범위, 코드리스트 준수, 정규식 패턴 등 검사 규칙 |
| **`codelist.csv`** | 4+ | **코드리스트**: 속성 검수에서 참조하는 공통 코드 정의서 |
| **`5_relation_check.csv`** | 5 | **관계 검수**: 레이어 간 위상 관계(포함, 교차, 이격 등) 및 공간 연관성 검사 규칙 |

---

## 공통 설정 규칙

### 규칙 비활성화 (주석 처리)
`RuleId` 또는 `TableId` 앞에 `#` 문자를 추가하면 해당 규칙은 **주석 처리되어 검수에서 제외**됩니다.
이는 `Enabled` 컬럼을 `N`으로 설정하는 것보다 더 우선적으로 적용되며, 임시로 규칙을 끌 때 유용합니다.

*   **적용 대상**: 1단계(테이블 검수), 4단계(속성 검수), 5단계(관계 검수)
*   **예시**: 
    - `#tn_mapindx_5k` (테이블 검수에서 제외)
    - `#LOG_TOP_REL_004` (관계 검수에서 제외)

### FieldFilter 필터 문법

관계 검수(5단계)에서 사용되는 필터 문법입니다:

| 형식 | 설명 | 예시 |
| :--- | :--- | :--- |
| `field_name` | 단순 필드 지정 | `road_se` |
| `field NOT IN ('val1','val2')` | 특정 값 제외 (SQL 스타일) | `road_se NOT IN ('RDS016','RDS017')` |
| `field IN ('val1','val2')` | 특정 값만 포함 (SQL 스타일) | `pg_rdfc_se IN ('PRC002','PRC003')` |
| `field=value` | 특정 값만 검사 | `road_se=RDS014` |
| `field;option=value` | 세미콜론 구분 옵션 | `road_se;exclude_road_types=RDS010,RDS011` |

---

## 상세 파일 구조

### 1. 테이블 검수 (`1_table_check.csv`)
File Geodatabase(GDB) 내에 반드시 존재해야 하는 테이블(레이어) 목록과 기본 속성을 정의합니다.

*   **TableId**: 테이블(Feature Class) 물리적 이름 (예: `tn_buld`)
*   **TableName**: 테이블 논리적 이름/한글명 (예: `건물`)
*   **GeometryType**: 기대하는 공간 타입 (`POLYGON`, `LINESTRING`, `POINT` 등)
*   **CRS**: 좌표계 정의 (예: `EPSG:5179`)

---

### 2. 스키마 검수 (`2_schema_check.csv`)
각 테이블의 컬럼(필드) 구조와 무결성 제약조건을 정의합니다.

*   **TableId**: 대상 테이블 ID
*   **FieldName**: 필드 영문명
*   **FieldAlias**: 필드 한글명/별칭
*   **DataType**: 데이터 타입 (`String`, `Integer`, `NUMERIC`, `Date` 등)
*   **Length**: 필드 길이 (문자열 길이 또는 `전체자리수,소수점자리수`)
*   **UK**: Unique Key 여부 (`Y` 또는 공백)
*   **FK**: Foreign Key 여부 (`FK` 또는 공백)
*   **NN**: Not Null 여부 (`Y` 또는 공백)
*   **RefTable**: FK인 경우 참조할 부모 테이블 ID
*   **RefColumn**: FK인 경우 참조할 부모 컬럼명

---

### 3. 지오메트리 검수 (`3_geometry_check.csv`)
각 테이블별로 수행할 지오메트리 검사 항목을 On/Off(`Y`/`N`) 합니다.

*   **TableId**: 대상 테이블 ID
*   **TableName**: 테이블 한글명
*   **GeometryType**: 지오메트리 타입
*   **검사 항목 컬럼들**:
    *   `객체중복`: 동일한 좌표를 가진 객체 검출
    *   `객체간겹침`: 서로 겹치는 객체 검출
    *   `자체꼬임`: Self-Intersection 검출
    *   `슬리버`: 매우 얇거나 작은 면적의 슬리버 폴리곤 검출
    *   `짧은객체`: 기준 길이 미만의 선 검출
    *   `작은면적객체`: 기준 면적 미만의 폴리곤 검출
    *   `홀 폴리곤 오류`: 폴리곤 내부 홀(Hole)의 위상 오류 검출
    *   `최소정점개수`: 구성 정점 수가 부족한 객체 검출
    *   `스파이크`: 급격하게 꺾이는 스파이크 형상 검출
    *   `자기중첩`: 링이나 선분이 자기 자신과 겹치는 경우
    *   `언더슛/오버슛`: 선 연결성 오류 (네트워크 데이터용)

#### 지오메트리 기준값 (`geometry_criteria.csv`)
3단계 검수에서 사용되는 전역 기준값입니다.

*   **항목명**: 기준 항목 (예: `최소선길이`, `겹침허용면적`)
*   **값**: 설정값 (예: `0.01`)
*   **단위**: 단위 설명 (예: `미터`, `제곱미터`)
*   **설명**: 항목에 대한 설명

---

## 4. 속성 검수 (`4_attribute_check.csv`) - CheckType 상세

필드 값의 논리적 유효성을 검사하는 규칙입니다.

### 컬럼 구조

| 컬럼명 | 설명 | 예시 |
| :--- | :--- | :--- |
| **RuleId** | 규칙 고유 식별자 | `LOG_DOM_ATR_003` |
| **Enabled** | 활성화 여부 | `Y` 또는 `N` |
| **TableId** | 대상 테이블 ID (`*`=전체) | `tn_buld`, `*` |
| **TableName** | 테이블 한글명 | `건물` |
| **FieldName** | 검사할 필드명 | `bldg_se` |
| **CheckType** | 검사 유형 | `CodeList`, `Range` 등 |
| **Parameters** | 검사 파라미터 | 검사 유형별 상이 |
| **Note** | 규칙 설명 | `건물 용도 코드리스트` |

---

### CheckType 유형 상세

#### 1. 기본 유효성 검사

##### `NotNull`
- **기능**: 필드 값이 NULL이 아니어야 함
- **적용 대상**: 필수 입력 필드
- **Parameters**: 없음
- **예시**:
  ```csv
  COM_OMS_ATR_006,Y,*,모든테이블,objectid,NotNull,,OBJECTID는 필수값
  ```

##### `NotZero`
- **기능**: 숫자 필드 값이 0이 아니어야 함
- **적용 대상**: 층수, 높이 등 0이 의미 없는 숫자 필드
- **Parameters**: 없음
- **예시**:
  ```csv
  LOG_DOM_ATR_002,Y,tn_buld,건물,bldg_nofl,NotZero,,층수 0 금지
  ```

---

#### 2. 숫자 범위/값 검사

##### `Range`
- **기능**: 숫자 값이 지정된 범위 내에 있는지 검사
- **적용 대상**: 높이, 좌표값 등 유효 범위가 있는 필드
- **Parameters**: `최소값..최대값` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_003,Y,tn_alpt,표고점,alpt_hgt,Range,0.0000001..1999.9999,표고점높이 0 초과 2000 미만
  ```
- **주의**: `0.0000001`은 0보다 큰 값을 의미 (0 제외)

##### `NumericEquals`
- **기능**: 숫자 값이 특정 값과 정확히 일치하는지 검사
- **적용 대상**: 특정 값이 오류인 경우 검출
- **Parameters**: 비교할 숫자값
- **예시**:
  ```csv
  LOG_DOM_ATR_004,Y,tn_alpt,표고점,alpt_hgt,NumericEquals,0,표고점높이가 정확히 0인 경우 오류
  ```

##### `MultipleOf`
- **기능**: 숫자 값이 특정 수의 배수인지 검사
- **적용 대상**: 등고선 높이값 등 규칙적인 간격이 있는 필드
- **Parameters**: 배수의 기준값
- **예시**:
  ```csv
  LOG_DOM_ATR_006,Y,tn_ctrln,등고선,ctrln_hgt,MultipleOf,5,등고선높이는 5의 배수
  ```

---

#### 3. 조건부 검사 (If-Then 패턴)

##### `IfCodeThenNotNullAll`
- **기능**: 특정 코드값일 때 지정된 다른 필드들이 모두 NULL이 아니어야 함
- **적용 대상**: 조건부 필수 필드 검사
- **Parameters**: `조건필드;조건값1|조건값2;필수필드1|필수필드2` 형식
- **예시**:
  ```csv
  COM_OMS_ATR_002,Y,tn_rodway_ctln,도로중심선,road_se,IfCodeThenNotNullAll,road_se;RDS001|RDS002|RDS003;FEAT_NM|ROAD_NO,고속국도/일반국도/지방도는 명칭과 번호 필수
  ```
- **동작**: `road_se`가 `RDS001`, `RDS002`, `RDS003` 중 하나이면 `FEAT_NM`과 `ROAD_NO`가 모두 NOT NULL이어야 함

##### `IfCodeThenNull`
- **기능**: 특정 코드값일 때 지정된 필드가 NULL이어야 함
- **적용 대상**: 특정 조건에서 값이 없어야 하는 필드
- **Parameters**: `조건필드;조건값;대상필드` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_014,Y,tn_buld,건물,feat_nm,IfCodeThenNull,bldg_se;BDG001;FEAT_NM,일반주택(BDG001)은 지형지물명칭이 NULL이어야 함
  ```

##### `IfCodeThenNumericEquals`
- **기능**: 특정 코드값일 때 숫자 필드가 지정된 값과 같아야 함
- **적용 대상**: 코드에 따라 고정값이 있는 경우
- **Parameters**: `조건필드;조건값;대상필드;기대값` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_008,Y,tn_rodway_ctln,도로중심선,road_bt,IfCodeThenNumericEquals,road_se;RDS014;ROAD_BT;1.5,소로(RDS014)는 도로폭 1.5m 고정
  ```

##### `IfCodeThenBetweenExclusive`
- **기능**: 특정 코드값일 때 숫자가 범위 내에 있어야 함 (경계값 제외)
- **적용 대상**: 코드별 유효 범위가 다른 경우
- **Parameters**: `조건필드;조건값;대상필드;최소..최대` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_009,Y,tn_rodway_ctln,도로중심선,road_bt,IfCodeThenBetweenExclusive,road_se;RDS010;ROAD_BT;1.5..3.0,면리간도로(RDS010)는 도로폭 1.5m 초과 3.0m 미만
  ```

##### `IfCodeThenGreaterThanOrEqual`
- **기능**: 특정 코드값일 때 숫자가 지정값 이상이어야 함
- **적용 대상**: 최소값 제약이 있는 경우
- **Parameters**: `조건필드;조건값1|조건값2;대상필드;최소값` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_013,Y,tn_rodway_ctln,도로중심선,road_bt,IfCodeThenGreaterThanOrEqual,road_se;RDS001|RDS002|...|RDS015;ROAD_BT;3.0,주요 도로는 도로폭 3.0m 이상
  ```

##### `IfCodeThenMultipleOf`
- **기능**: 특정 코드값일 때 숫자가 지정된 배수여야 함
- **적용 대상**: 코드별로 다른 배수 규칙이 있는 경우
- **Parameters**: `조건필드;조건값1|조건값2;대상필드;배수기준` 형식
- **예시**:
  ```csv
  LOG_DOM_ATR_007,Y,tn_ctrln,등고선,ctrln_hgt,IfCodeThenMultipleOf,ctrln_se;CTS001|CTS101;CTRLN_HGT;25,계곡선(CTS001/CTS101)은 높이가 25의 배수
  ```

---

#### 4. 건물 높이 특화 검사

##### `buld_height_base_vs_max`
- **기능**: 건물 기본높이가 최고높이보다 큰 경우 오류
- **적용 대상**: `tn_buld` 테이블
- **Parameters**: 없음
- **검사 로직**: `bldbsc_hgt` > `bldhgt_hgt` 이면 오류

##### `buld_height_max_vs_facility`
- **기능**: 건물 최고높이가 시설물높이보다 큰 경우 오류
- **적용 대상**: `tn_buld` 테이블
- **Parameters**: 없음
- **검사 로직**: `bldhgt_hgt` > `blfcht_hgt` 이면 오류

##### `buld_height_lowest_vs_base`
- **기능**: 신규건물 중 최저높이가 기본높이보다 큰 경우 오류
- **적용 대상**: `tn_buld` 테이블의 신규건물
- **Parameters**: 없음
- **검사 로직**: `objfltn_se='OBF001'`이고 `bldlwt_hgt` > `bldbsc_hgt` 이면 오류

---

#### 5. 형식/패턴 검사

##### `Regex`
- **기능**: 정규식 패턴과 일치하는지 검사
- **적용 대상**: PNU, NFID 등 형식이 정해진 필드
- **Parameters**: 정규식 패턴
- **예시**:
  ```csv
  LOG_FMT_ATR_001,N,tn_buld,건물,pnu,Regex,^[0-9]{19}$,PNU는 19자리 숫자
  LOG_FMT_ATR_002,Y,*,모든테이블,nf_id,Regex,^[A-Z0-9]{17}$,NFID는 17자리 영숫자
  ```

##### `KoreanTypo`
- **기능**: 한글 필드에서 자모 분리, 특수문자 오류 등 검사
- **적용 대상**: 지형지물명칭, 부명칭 등 한글 텍스트 필드
- **Parameters**: 없음
- **예시**:
  ```csv
  THE_ATT_ATR_001,Y,*,전체,feat_nm,KoreanTypo,,지형지물명칭 오타 검사
  ```

---

#### 6. 코드리스트 검사

##### `CodeList`
- **기능**: 필드값이 `codelist.csv`에 정의된 유효한 코드인지 검사
- **적용 대상**: 도메인 코드 필드 (구분코드, 유형코드 등)
- **Parameters**: `codelist.csv`의 `CodeSetId` 값
- **예시**:
  ```csv
  THE_CLS_ATR_001,Y,tn_buld,건물,bldg_se,CodeList,건물구분,건물구분 코드리스트 검사
  THE_CLS_ATR_014_01,Y,tn_rodway_bndry,도로경계면,road_se,CodeList,도로구분,도로구분 코드리스트 검사
  ```
- **동작**: `codelist.csv`에서 `CodeSetId`가 `건물구분`인 행들의 `CodeValue` 목록과 비교

---

## 5. 관계 검수 (`5_relation_check.csv`) - CaseType 상세

두 레이어(Feature Class) 간의 공간적 위상 관계를 검사합니다.

### 컬럼 구조

| 컬럼명 | 설명 | 예시 |
| :--- | :--- | :--- |
| **RuleId** | 규칙 고유 식별자 | `LOG_TOP_REL_001` |
| **Enabled** | 활성화 여부 | `Y` 또는 `N` |
| **CaseType** | 관계 검사 유형 | `PolygonNotOverlap` |
| **MainTableId** | 주 검사 대상 테이블 | `tn_buld` |
| **MainTableName** | 주 테이블 한글명 | `건물` |
| **RelatedTableId** | 관계 비교 대상 테이블 | `tn_rodway_bndry` |
| **RelatedTableName** | 관계 테이블 한글명 | `도로경계면` |
| **FieldFilter** | 속성 필터 또는 검사 옵션 | `road_se NOT IN (...)` |
| **Tolerance** | 허용 오차 (미터) | `0.01` |
| **Note** | 규칙 설명 | `건물_도로경계면_침범` |

---

### CaseType 유형 상세

#### 1. 폴리곤 관계 검사

##### `PolygonNotOverlap`
- **기능**: 두 폴리곤 레이어가 서로 겹치면 안 됨
- **적용 대상**: 건물-도로, 건물-하천 등 겹침 금지 관계
- **FieldFilter**: 특정 코드만 검사할 때 SQL 형식 조건
- **Tolerance**: 허용 겹침 면적 (㎡)
- **예시**:
  ```csv
  LOG_TOP_REL_003,Y,PolygonNotOverlap,tn_buld,건물,tn_rodway_bndry,도로경계면,,0.1,건물은 도로경계면과 0.1㎡ 이상 겹치면 오류
  ```

##### `PolygonWithinPolygon`
- **기능**: MainTable 폴리곤이 RelatedTable 폴리곤 내부에 포함되어야 함
- **적용 대상**: 교량이 도로경계면 내부에 있어야 하는 경우 등
- **FieldFilter**: 특정 시설물 코드 필터 (`IN` 조건)
- **Tolerance**: 허용 오차
- **예시**:
  ```csv
  LOG_TOP_REL_024,Y,PolygonWithinPolygon,tn_arrfc,면형도로시설,tn_rodway_bndry,도로경계면,"pg_rdfc_se IN ('PRC002','PRC003','PRC004','PRC005',...)",0.001,교량/터널은 도로경계면 내부에 있어야 함
  ```

##### `PolygonNotWithinPolygon`
- **기능**: MainTable 폴리곤이 RelatedTable 폴리곤 내부에 완전히 포함되면 안 됨
- **적용 대상**: 버텍스 일치 검사 등
- **예시**:
  ```csv
  LOG_TOP_REL_025,Y,PolygonNotWithinPolygon,tn_arrfc,면형도로시설,tn_rodway_bndry,도로경계면,"pg_rdfc_se IN ('PRC002',...)",0.001,면형도로시설 버텍스가 도로경계면 버텍스와 일치해야 함
  ```

##### `PolygonMissingLine`
- **기능**: 폴리곤 경계면 내부에 중심선이 하나도 없는 경우 오류
- **적용 대상**: 도로경계면에 도로중심선이 없는 경우 검출
- **FieldFilter**: 제외할 도로구분 코드 (`NOT IN` 조건)
- **예시**:
  ```csv
  COM_OMS_REL_001,Y,PolygonMissingLine,tn_rodway_bndry,도로경계면,tn_rodway_ctln,도로중심선,"road_se NOT IN ('RDS016','RDS017')",0.1,경계면 내부에 중심선이 없으면 오류
  ```

##### `PolygonContainsObjects`
- **기능**: 폴리곤 내부에 포함되거나 겹치는 모든 객체 검출
- **적용 대상**: 경지경계 내부 객체 검사 등
- **RelatedTableId**: `*` (모든 레이어)
- **예시**:
  ```csv
  LOG_TOP_REL_016,Y,PolygonContainsObjects,tn_fmlnd_bndry,경지경계,*,모든객체,,0,경지경계 내부의 모든 객체 검출
  ```

##### `PolygonBoundaryMatch`
- **기능**: 폴리곤 외곽선과 선형 레이어가 일치하는지 검사
- **적용 대상**: 도로경계면-도로경계선 일치 검사
- **Tolerance**: 불일치 허용 거리 (m)
- **예시**:
  ```csv
  LOG_TOP_REL_023,Y,PolygonBoundaryMatch,tn_rodway_bndryln,도로경계선,tn_rodway_bndry,도로경계면,,0.1,도로경계선이 도로경계면 외곽과 일치해야 함
  ```

##### `PolygonIntersectionWithAttribute`
- **기능**: 동일 속성값을 가진 폴리곤 간 교차 검사
- **적용 대상**: 같은 도로구분의 도로경계면끼리 겹치면 안 됨
- **FieldFilter**: 비교할 속성 필드명
- **예시**:
  ```csv
  LOG_TOP_REL_037,Y,PolygonIntersectionWithAttribute,tn_rodway_bndry,도로경계면,tn_rodway_bndry,도로경계면,road_se,0.01,동일 도로구분 경계면 간 교차 검사
  ```

---

#### 2. 폴리곤-선형 관계 검사

##### `PolygonNotIntersectLine`
- **기능**: 폴리곤과 선형 레이어가 교차하면 안 됨
- **적용 대상**: 건물-등고선, 경지경계-도로경계선 등
- **예시**:
  ```csv
  LOG_TOP_REL_018,Y,PolygonNotIntersectLine,tn_fmlnd_bndry,경지경계,tn_rodway_bndryln,도로경계선,,,경지경계가 도로경계선과 교차하면 오류
  ```

##### `LineWithinPolygon`
- **기능**: 선형이 폴리곤 내부에 완전히 포함되어야 함
- **적용 대상**: 도로중심선이 도로경계면 내부에 있어야 함
- **FieldFilter**: 제외할 도로구분 (`NOT IN` 조건)
- **Tolerance**: 허용 이탈 거리 (m)
- **예시**:
  ```csv
  LOG_TOP_REL_021,Y,LineWithinPolygon,tn_rodway_bndry,도로경계면,tn_rodway_ctln,도로중심선,"road_se NOT IN ('RDS010','RDS011',...)",0.001,중심선이 경계면을 벗어나면 오류
  ```
- **필터 설명**: `RDS010(면리간도로)`, `RDS011(부지안도로)` 등은 경계면 없이 존재할 수 있어 제외

##### `LineEndpointWithinPolygon`
- **기능**: 선형의 양 끝점이 폴리곤 내부에 있어야 함 (초과/미달 검사)
- **적용 대상**: 중심선 끝점이 경계면 내부에 있어야 함
- **Tolerance**: 끝점 허용 이탈 거리 (m)
- **예시**:
  ```csv
  LOG_TOP_REL_032,Y,LineEndpointWithinPolygon,tn_rodway_ctln,도로중심선,tn_rodway_bndry,도로경계면,,0.3,중심선 끝점이 경계면 내부에 있어야 함
  ```

---

#### 3. 폴리곤-점형 관계 검사

##### `PolygonNotContainPoint`
- **기능**: 폴리곤 내부에 점이 포함되면 안 됨
- **적용 대상**: 건물 내부에 수로시설/표고점 포함 금지
- **예시**:
  ```csv
  LOG_TOP_REL_011,Y,PolygonNotContainPoint,tn_buld,건물,tn_alpt,표고점,,,건물 내부에 표고점이 있으면 오류
  ```

##### `PointInsidePolygon`
- **기능**: 점이 폴리곤 내부에 있어야 함
- **적용 대상**: 건물중심점이 건물 내부에 있어야 함
- **예시**:
  ```csv
  LOG_TOP_REL_014,Y,PointInsidePolygon,tn_buld,건물,tn_buld_ctpt,건물중심점,,,건물중심점은 건물 내부에 있어야 함
  ```

---

#### 4. 선형 연결성/단절 검사

##### `LineConnectivity`
- **기능**: 선형 끝점 간 연결성 검사 (언더슛/댕글 검출)
- **적용 대상**: 도로중심선 네트워크 연결성
- **Tolerance**: 끝점 간 허용 거리 (m)
- **예시**:
  ```csv
  LOG_TOP_REL_028,Y,LineConnectivity,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,,1,끝점이 1m 이내에 연결되지 않으면 검출
  ```

##### `LineConnectivityWithFilter`
- **기능**: 특정 코드의 선형이 다른 선형과 연결되어 있는지 검사
- **적용 대상**: 소로가 다른 도로와 연결되어야 함
- **FieldFilter**: 검사 대상 코드 조건 (`필드=값` 형식)
- **예시**:
  ```csv
  LOG_TOP_REL_035,Y,LineConnectivityWithFilter,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,road_se=RDS014,1,소로(RDS014)가 다른 도로와 연결되어야 함
  ```

##### `LineDisconnection`
- **기능**: 선형이 중간에 단절된 경우 검출
- **적용 대상**: 도로중심선 단절 검사
- **FieldFilter**: 제외할 코드 (`NOT IN` 조건)
- **예시**:
  ```csv
  LOG_TOP_REL_027,Y,LineDisconnection,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,"road_se NOT IN ('RDS010','RDS011',...)",1,도로중심선이 단절되면 오류
  ```

##### `LineDisconnectionWithAttribute`
- **기능**: 동일 속성값을 가진 선형 간 단절 검사
- **적용 대상**: 같은 도로구분의 도로경계선이 단절되면 오류
- **FieldFilter**: `속성필드;exclude_road_types=제외코드1,제외코드2` 형식
- **예시**:
  ```csv
  LOG_TOP_REL_022,Y,LineDisconnectionWithAttribute,tn_rodway_bndryln,도로경계선,tn_rodway_bndryln,도로경계선,"road_se;exclude_road_types=RDS010,RDS011,...",1,동일 ROAD_SE 도로경계선 단절 검사
  ```

##### `DefectiveConnection`
- **기능**: 중심선 연결 결함 검사 (끝점이 다른 중심선 또는 경계면에 붙어있어야 함)
- **적용 대상**: 도로중심선 연결 결함
- **FieldFilter**: 제외할 코드 (`NOT IN` 조건)
- **예시**:
  ```csv
  LOG_TOP_REL_015,Y,DefectiveConnection,tn_rodway_ctln,도로중심선,tn_rodway_bndry,도로경계면,"road_se NOT IN ('RDS010','RDS011',...)",1,중심선 끝점이 다른 중심선/경계면에 붙어있어야 함
  ```

---

#### 5. 선형 교차/꺾임 검사

##### `LineIntersectionWithAttribute`
- **기능**: 동일 속성값을 가진 선형 간 교차 검사
- **적용 대상**: 같은 도로구분의 도로중심선끼리 교차하면 오류
- **FieldFilter**: 비교할 속성 필드명
- **예시**:
  ```csv
  LOG_TOP_REL_038,Y,LineIntersectionWithAttribute,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,road_se,0.01,동일 도로구분 중심선 간 교차 검사
  ```

##### `RoadSharpBend`
- **기능**: 도로중심선의 급격한 꺾임 검출
- **적용 대상**: 도로중심선
- **Tolerance**: 최소 허용 각도 (도)
- **예시**:
  ```csv
  LOG_TOP_GEO_013,Y,RoadSharpBend,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,,6,6도 이하 꺾임 검출
  ```

##### `ContourSharpBend`
- **기능**: 등고선의 급격한 꺾임 검출
- **적용 대상**: 등고선
- **Tolerance**: 최소 허용 각도 (도)
- **예시**:
  ```csv
  LOG_TOP_GEO_014,Y,ContourSharpBend,tn_ctrln,등고선,tn_ctrln,등고선,,90,90도 미만 꺾임 검출
  ```

##### `ContourIntersection`
- **기능**: 등고선끼리 교차하면 오류
- **적용 대상**: 등고선
- **예시**:
  ```csv
  LOG_TOP_REL_029,Y,ContourIntersection,tn_ctrln,등고선,tn_ctrln,등고선,,,등고선 간 교차 검사
  ```

---

#### 6. 속성 관계 검사

##### `CenterlineAttributeMismatch`
- **기능**: 연결된 중심선 간 속성 연속성 검사
- **적용 대상**: 연결된 도로/철도/하천 중심선의 속성 일치 여부
- **FieldFilter**: `검사필드1|검사필드2|검사필드3;옵션1=값1;옵션2=값2` 형식
  - `intersection_threshold`: 교차점 허용 거리
  - `angle_threshold`: 연결 인정 각도
  - `exclude_road_types`: 제외할 도로구분 코드
- **예시**:
  ```csv
  LOG_CNC_REL_001,Y,CenterlineAttributeMismatch,tn_rodway_ctln,도로중심선,tn_rodway_ctln,도로중심선,"road_no|feat_nm|road_se;intersection_threshold=2;angle_threshold=30;exclude_road_types=RDS010,RDS011,...",1,연결된 도로중심선의 도로번호/명칭/구분이 일치해야 함
  ```

##### `ConnectedLinesSameAttribute`
- **기능**: 연결된 선형 간 특정 속성이 동일해야 함
- **적용 대상**: 연결된 등고선의 높이값 일치
- **FieldFilter**: 비교할 속성 필드명
- **예시**:
  ```csv
  LOG_CNC_REL_002,Y,ConnectedLinesSameAttribute,tn_ctrln,등고선,tn_ctrln,등고선,ctrln_hgt,1,연결된 등고선끼리 높이값이 같아야 함
  ```

##### `AttributeSpatialMismatch`
- **기능**: 공간적으로 겹치는 객체 간 속성 관계 검사
- **적용 대상**: 도로경계면과 도로시설의 속성 관계
- **FieldFilter**: `메인속성;관련속성` 형식
- **예시**:
  ```csv
  LOG_CNC_REL_003,Y,AttributeSpatialMismatch,tn_rodway_bndry,도로경계면,tn_arrfc,면형도로시설,road_se;pg_rdfc_se,0.01,도로경계면에 중첩된 도로시설의 속성 관계 검사
  ```

##### `BridgeRiverNameMatch`
- **기능**: 교량과 교차하는 하천의 명칭 일치 검사
- **적용 대상**: 교량-하천 명칭 일치
- **FieldFilter**: `필터조건;명칭필드` 형식
- **예시**:
  ```csv
  THE_CLS_REL_001,N,BridgeRiverNameMatch,tn_arrfc,면형도로시설,tn_river_ctln,하천중심선,"pg_rdfc_se IN ('PRC002',...);feat_nm",1,교량 명칭에 하천명이 포함되어야 함
  ```

---

#### 7. 특수 검사

##### `PointSpacingCheck`
- **기능**: 점 객체 간 최소 거리 검사 (축척별, 지형별 상이)
- **적용 대상**: 표고점 간격 검사
- **FieldFilter**: `scale=축척;flatland=평지거리;road_sidewalk=인도거리;road_carriageway=차도거리` 형식
- **예시**:
  ```csv
  LOG_TOP_REL_039,N,PointSpacingCheck,tn_alpt,표고점,tn_alpt,표고점,scale=5K;flatland=200;road_sidewalk=20;road_carriageway=30,0,표고점 간 최소 거리 검사
  ```

##### `HoleDuplicateCheck`
- **기능**: 폴리곤의 홀(구멍)과 동일한 형상의 다른 객체가 있는지 검사
- **적용 대상**: 모든 폴리곤 레이어
- **MainTableId/RelatedTableId**: `*` (모든 레이어)
- **Tolerance**: 형상 일치 허용 오차
- **예시**:
  ```csv
  LOG_TOP_REL_040,Y,HoleDuplicateCheck,*,모든폴리곤,*,모든객체,,0.001,폴리곤의 홀과 동일한 객체 존재 여부 검사
  ```

---

## 도로구분 코드 참고표

| 코드 | 명칭 | 비고 |
| :--- | :--- | :--- |
| RDS001 | 고속국도 | 주요 도로 |
| RDS002 | 일반국도 | 주요 도로 |
| RDS003 | 지방도 | 주요 도로 |
| RDS004 | 특별시도 | 주요 도로 |
| RDS005 | 광역시도 | 주요 도로 |
| RDS006 | 시도 | 주요 도로 |
| RDS007 | 군도 | 주요 도로 |
| RDS008 | 구도 | 주요 도로 |
| RDS010 | 면리간도로 | 경계면 없이 존재 가능 |
| RDS011 | 부지안도로 | 경계면 없이 존재 가능 |
| RDS013 | 터널안도로 | 터널 내부 |
| RDS014 | 소로 | 소규모 도로 |
| RDS015 | 주요도로 | 주요 도로 |
| RDS016 | 부지안보행노면 | 보행자 전용 |
| RDS017 | 부지안경계선 | 경계선 |
| RDS018 | 부지안보행노선 | 보행자 전용 |

---

## 수정 시 주의사항

1. **CSV 파일 인코딩**: UTF-8 (BOM 포함) 권장
2. **쉼표 처리**: 값에 쉼표가 포함된 경우 큰따옴표로 감싸기
3. **필터 문법**: SQL 스타일 필터는 작은따옴표 사용 (`'RDS001'`)
4. **Tolerance 단위**: 미터(m) 또는 제곱미터(㎡)
5. **규칙 비활성화**: `Enabled=N` 또는 `RuleId` 앞에 `#` 추가
6. **와일드카드**: `*`는 모든 테이블 대상을 의미

