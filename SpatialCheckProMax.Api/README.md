# SpatialCheckProMax API

FileGDB 검수 및 Shapefile 변환 REST API 서비스

## 🚀 시작하기

### 실행
```bash
cd SpatialCheckProMax.Api
dotnet run
```

### Swagger UI
브라우저에서 `http://localhost:5000` 접속

---

# 📋 검수 API (Validation)

## 엔드포인트

### 검수 단계 정보
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `GET` | `/api/Validation/stages` | 사용 가능한 검수 단계 목록 |

### 검수 실행
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `POST` | `/api/Validation/start` | 비동기 검수 시작 |
| `POST` | `/api/Validation/validate` | 동기 검수 (소규모용) |

### 작업 관리
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `GET` | `/api/Validation/jobs` | 전체 검수 작업 목록 |
| `GET` | `/api/Validation/jobs/{jobId}/status` | 검수 진행 상황 조회 |
| `GET` | `/api/Validation/jobs/{jobId}/result` | 검수 결과 조회 |
| `GET` | `/api/Validation/jobs/{jobId}/errors` | 오류 목록 조회 (페이징) |
| `POST` | `/api/Validation/jobs/{jobId}/cancel` | 검수 취소 |
| `DELETE` | `/api/Validation/jobs/{jobId}` | 검수 작업 삭제 |

---

## 검수 단계

| 단계 | 이름 | 설명 |
|------|------|------|
| 1 | 테이블 검수 | 테이블 리스트, 좌표계, 지오메트리 타입 검증 |
| 2 | 스키마 검수 | 컬럼 구조, 데이터 타입, PK/FK 검증 |
| 3 | 지오메트리 검수 | 중복, 겹침, 꼬임, 슬리버 폴리곤 검사 |
| 4 | 관계 검수 | 테이블 간 공간 관계 검증 |

---

## 사용 예시

### 1. 검수 단계 정보 조회

```bash
curl http://localhost:5000/api/Validation/stages
```

**응답:**
```json
[
  {
    "stageNumber": 1,
    "stageName": "테이블 검수",
    "description": "테이블 리스트, 좌표계, 지오메트리 타입 검증",
    "checkTypes": ["TABLE_LIST_CHECK", "COORDINATE_SYSTEM_CHECK", "GEOMETRY_TYPE_CHECK"]
  },
  ...
]
```

### 2. 비동기 검수 시작

```bash
curl -X POST http://localhost:5000/api/Validation/start \
  -H "Content-Type: application/json" \
  -d '{
    "gdbPath": "C:/data/input.gdb",
    "stages": [1, 2, 3, 4],
    "stopOnTableCheckFailure": true
  }'
```

**응답:**
```json
{
  "success": true,
  "jobId": "val_20241201_143022_abc12345",
  "startedAt": "2024-12-01T14:30:22",
  "selectedStages": [1, 2, 3, 4]
}
```

### 3. 특정 단계만 검수

```bash
curl -X POST http://localhost:5000/api/Validation/start \
  -H "Content-Type: application/json" \
  -d '{
    "gdbPath": "C:/data/input.gdb",
    "stages": [3, 4]
  }'
```

### 4. 진행 상황 조회

```bash
curl http://localhost:5000/api/Validation/jobs/val_20241201_143022_abc12345/status
```

**응답:**
```json
{
  "jobId": "val_20241201_143022_abc12345",
  "state": "Running",
  "progress": 45.5,
  "currentStage": 2,
  "currentStageName": "스키마 검수",
  "currentTask": "컬럼 구조 검증 중",
  "errorCount": 3,
  "warningCount": 12,
  "elapsedTime": "00:02:15",
  "stageProgress": [
    { "stageNumber": 1, "stageName": "테이블 검수", "status": "Completed", "progress": 100 },
    { "stageNumber": 2, "stageName": "스키마 검수", "status": "Running", "progress": 60 },
    { "stageNumber": 3, "stageName": "지오메트리 검수", "status": "Pending", "progress": 0 },
    { "stageNumber": 4, "stageName": "관계 검수", "status": "Pending", "progress": 0 }
  ]
}
```

### 5. 검수 결과 조회

```bash
curl http://localhost:5000/api/Validation/jobs/val_20241201_143022_abc12345/result
```

**응답:**
```json
{
  "jobId": "val_20241201_143022_abc12345",
  "success": true,
  "status": "Completed",
  "targetFile": "C:/data/input.gdb",
  "totalErrors": 15,
  "totalWarnings": 42,
  "duration": "00:05:23",
  "tableCheck": { "stageNumber": 1, "status": "Passed", "errorCount": 0 },
  "schemaCheck": { "stageNumber": 2, "status": "Passed", "errorCount": 3 },
  "geometryCheck": { "stageNumber": 3, "status": "Failed", "errorCount": 8 },
  "relationCheck": { "stageNumber": 4, "status": "Passed", "errorCount": 4 },
  "summary": {
    "totalStages": 4,
    "completedStages": 3,
    "failedStages": 1,
    "totalChecks": 12,
    "passedChecks": 10,
    "failedChecks": 2
  }
}
```

### 6. 오류 목록 조회 (페이징)

```bash
# 전체 오류
curl "http://localhost:5000/api/Validation/jobs/val_20241201_143022_abc12345/errors?page=1&pageSize=50"

# 특정 단계 오류만
curl "http://localhost:5000/api/Validation/jobs/val_20241201_143022_abc12345/errors?stage=3&page=1&pageSize=50"
```

---

## 🐍 Python 클라이언트 예시

```python
import requests
import time

BASE_URL = "http://localhost:5000/api/Validation"

# 1. 검수 단계 정보 조회
stages = requests.get(f"{BASE_URL}/stages").json()
print(f"사용 가능한 단계: {[s['stageName'] for s in stages]}")

# 2. 검수 시작 (3단계, 4단계만)
response = requests.post(f"{BASE_URL}/start", json={
    "gdbPath": "C:/data/input.gdb",
    "stages": [3, 4]  # 지오메트리, 관계 검수만
})
job_id = response.json()["jobId"]
print(f"검수 시작: {job_id}")

# 3. 진행 상황 모니터링
while True:
    status = requests.get(f"{BASE_URL}/jobs/{job_id}/status").json()
    
    print(f"진행률: {status['progress']:.1f}% - {status['currentStageName']}: {status['currentTask']}")
    print(f"  오류: {status['errorCount']}, 경고: {status['warningCount']}")
    
    # 단계별 상태 출력
    for stage in status['stageProgress']:
        print(f"  [{stage['stageName']}] {stage['status']} ({stage['progress']:.0f}%)")
    
    if status["state"] in ["Completed", "Failed", "Cancelled"]:
        break
    
    time.sleep(2)

# 4. 결과 확인
if status["state"] == "Completed":
    result = requests.get(f"{BASE_URL}/jobs/{job_id}/result").json()
    print(f"\n검수 완료!")
    print(f"총 오류: {result['totalErrors']}, 총 경고: {result['totalWarnings']}")
    print(f"소요 시간: {result['duration']}")
    
    # 오류 목록 조회
    errors = requests.get(f"{BASE_URL}/jobs/{job_id}/errors?pageSize=10").json()
    print(f"\n오류 목록 ({errors['totalCount']}건):")
    for err in errors['errors'][:5]:
        print(f"  [{err['errorCode']}] {err['message']}")
```

---

# 📦 변환 API (ShpConvert)

## 엔드포인트

### 분석
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `POST` | `/api/ShpConvert/analyze` | GDB 레이어 분석 |

### 변환
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `POST` | `/api/ShpConvert/start` | 비동기 변환 시작 |
| `POST` | `/api/ShpConvert/convert` | 동기 변환 (소규모용) |

### 작업 관리
| 메서드 | 경로 | 설명 |
|--------|------|------|
| `GET` | `/api/ShpConvert/jobs` | 전체 작업 목록 |
| `GET` | `/api/ShpConvert/jobs/{jobId}/status` | 작업 상태 조회 |
| `GET` | `/api/ShpConvert/jobs/{jobId}/result` | 변환 결과 조회 |
| `POST` | `/api/ShpConvert/jobs/{jobId}/cancel` | 작업 취소 |
| `DELETE` | `/api/ShpConvert/jobs/{jobId}` | 작업 삭제 |

---

## 변환 사용 예시

```python
import requests
import time

BASE_URL = "http://localhost:5000/api/ShpConvert"

# 1. GDB 분석
response = requests.post(f"{BASE_URL}/analyze", json={
    "gdbPath": "C:/data/input.gdb"
})
analysis = response.json()
print(f"총 레이어: {analysis['totalLayers']}, 예상 용량: {analysis['totalEstimatedSize']}")

# 2. 변환 시작
response = requests.post(f"{BASE_URL}/start", json={
    "gdbPath": "C:/data/input.gdb",
    "outputPath": "C:/data/output",
    "selectedLayers": ["BUILDING", "ROAD"],
    "targetFileSizeMB": 1300
})
job_id = response.json()["jobId"]

# 3. 진행 상황 모니터링
while True:
    status = requests.get(f"{BASE_URL}/jobs/{job_id}/status").json()
    print(f"진행률: {status['progress']:.1f}% - {status['statusMessage']}")
    
    if status["state"] in ["Completed", "Failed"]:
        break
    time.sleep(2)

# 4. 결과 확인
result = requests.get(f"{BASE_URL}/jobs/{job_id}/result").json()
print(f"생성 파일: {result['totalFilesCreated']}개")
```

---

# ⚙️ 설정

### appsettings.json
```json
{
  "Urls": "http://+:5000",
  "ValidationConfigDirectory": "./Config",
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### 포트 변경
```bash
dotnet run --urls "http://+:8080"
```

---

# 📦 배포

### 빌드
```bash
dotnet publish -c Release -o ./publish
```

### Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
COPY publish/ /app/
COPY Config/ /app/Config/
WORKDIR /app
EXPOSE 5000
ENTRYPOINT ["dotnet", "SpatialCheckProMax.Api.dll"]
```

---

# ⚠️ 주의사항

1. **GDAL 라이브러리**: API 서버에 GDAL 네이티브 라이브러리 필요
2. **Config 디렉토리**: 검수 설정 CSV 파일 필요
3. **파일 경로**: API 서버가 접근 가능한 경로 사용
4. **대용량 처리**: 비동기 API 사용 권장 (`/start`)
5. **작업 정리**: 완료된 작업은 24시간 후 자동 삭제

---

# 📝 응답 코드

| 코드 | 설명 |
|------|------|
| 200 | 성공 |
| 202 | 작업 시작됨 (비동기) |
| 400 | 잘못된 요청 |
| 404 | 리소스 없음 |
| 500 | 서버 오류 |

