using SpatialCheckProMax.Api.Services;
using SpatialCheckProMax.Services;
using SpatialCheckProMax.Processors;
using SpatialCheckProMax.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Swagger/OpenAPI 설정
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SpatialCheckProMax API",
        Version = "v1",
        Description = "FileGDB 검수 및 Shapefile 변환 REST API 서비스\n\n" +
            "### 주요 기능\n" +
            "- **검수 API**: 5단계 공간데이터 품질 검수 (테이블, 스키마, 지오메트리, 속성관계, 공간관계)\n" +
            "- **변환 API**: FileGDB → Shapefile 변환\n" +
            "- 비동기 작업 관리 및 진행 상황 조회",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "SpatialCheckProMax"
        }
    });

    // XML 문서 포함 (옵션)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// ============================================
// SHP 변환 서비스 등록
// ============================================
builder.Services.AddSingleton<IJobManager, InMemoryJobManager>();
builder.Services.AddScoped<IShpConvertService, ShpConvertService>();

// ============================================
// 검수 서비스 등록 (SpatialCheckProMax 라이브러리)
// ============================================

// 기본 서비스
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<ICsvConfigService, CsvConfigService>();
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();

// 검수 프로세서 (5단계)
builder.Services.AddScoped<ITableCheckProcessor, TableCheckProcessor>();      // 1단계: 테이블 검수
builder.Services.AddScoped<ISchemaCheckProcessor, SchemaCheckProcessor>();    // 2단계: 스키마 검수
builder.Services.AddScoped<IGeometryCheckProcessor, GeometryCheckProcessor>();// 3단계: 지오메트리 검수
builder.Services.AddScoped<IAttributeCheckProcessor, AttributeCheckProcessor>(); // 4단계: 속성 관계 검수
builder.Services.AddScoped<IRelationCheckProcessor, RelationCheckProcessor>();// 5단계: 공간 관계 검수

// 검수 결과 서비스
builder.Services.AddScoped<IValidationResultService, ValidationResultService>();

// 메인 검수 서비스
builder.Services.AddScoped<IValidationService, ValidationService>();

// 검수 작업 관리자
builder.Services.AddSingleton<IValidationJobManager, InMemoryValidationJobManager>();

// 공간 인덱스 서비스 (검수에 필요)
builder.Services.AddSingleton<IMemoryManager, MemoryManager>();
builder.Services.AddScoped<ISpatialIndexManager, SpatialIndexManager>();
builder.Services.AddTransient<RTreeSpatialIndex>();
builder.Services.AddTransient<OptimizedRTreeSpatialIndex>();
builder.Services.AddTransient<QuadTreeSpatialIndex>();
builder.Services.AddTransient<GridSpatialIndex>();

// ============================================
// CORS 설정
// ============================================
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 로깅 설정
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SpatialCheckProMax API v1");
        options.RoutePrefix = string.Empty; // Swagger UI를 루트에서 접근
    });
}

// Production에서도 Swagger 활성화 (선택사항)
if (!app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SpatialCheckProMax API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

// 시작 로그
app.Logger.LogInformation("========================================");
app.Logger.LogInformation("SpatialCheckProMax API 서버 시작");
app.Logger.LogInformation("========================================");
app.Logger.LogInformation("Swagger UI: http://localhost:5000");
app.Logger.LogInformation("검수 API: /api/Validation");
app.Logger.LogInformation("변환 API: /api/ShpConvert");
app.Logger.LogInformation("========================================");

app.Run();

