using Microsoft.Extensions.Logging;
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// GDAL 라이브러리 초기화 서비스
    /// </summary>
    public interface IGdalInitializationService
    {
        /// <summary>
        /// GDAL 초기화 상태
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// GDAL 초기화
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// GDAL 라이브러리 초기화 서비스 구현
    /// </summary>
    public class GdalInitializationService : IGdalInitializationService
    {
        private readonly ILogger<GdalInitializationService> _logger;
        private static bool _isInitialized = false;
        private static readonly object _initLock = new object();

        // Windows API for DLL directory management
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AddDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

        /// <summary>
        /// GDAL 초기화 상태
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 생성자
        /// </summary>
        public GdalInitializationService(ILogger<GdalInitializationService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// GDAL 초기화
        /// </summary>
        public void Initialize()
        {
            try
            {
                lock (_initLock)
                {
                    if (_isInitialized)
                    {
                        _logger.LogDebug("GDAL이 이미 초기화되어 있습니다.");
                        if (Gdal.GetDriverCount() > 0)
                        {
                            return;
                        }
                    }

                    try
                    {
                        _logger.LogInformation("GDAL 3.10.0 (MaxRev.Gdal.Core) 초기화 시작...");

                        // PostgreSQL/PostGIS PROJ 충돌 방지를 위해 먼저 PATH 정리
                        CleanupConflictingProjPaths();
                        
                        // MaxRev.Gdal.Core의 실제 PROJ 경로를 먼저 설정
                        SetupMaxRevProjPath();

                        // MaxRev.Gdal.Core 초기화 - 모든 설정을 자동으로 처리
                        GdalBase.ConfigureAll();
                        _logger.LogInformation("MaxRev.Gdal.Core ConfigureAll() 완료");

                        // PROJ 라이브러리 경로 설정 (추가 설정이 필요한 경우)
                        SetupProjLibrary();

                        // 전체 환경 설정 (추가 설정이 필요한 경우)
                        ConfigureEnvironment();

                        _logger.LogInformation("GDAL/OGR 초기화 성공");

                        Gdal.UseExceptions();
                        Ogr.UseExceptions();
                        Osr.UseExceptions();

                        // 드라이버 확인
                        int driverCount = Gdal.GetDriverCount();
                        _logger.LogInformation("사용 가능한 GDAL 드라이버 수: {DriverCount}", driverCount);

                        // OpenFileGDB 드라이버 확인
                        var openFileGDBDriver = Ogr.GetDriverByName("OpenFileGDB");
                        if (openFileGDBDriver != null)
                        {
                            _logger.LogInformation("OpenFileGDB 드라이버가 사용 가능합니다.");
                        }
                        else
                        {
                            _logger.LogWarning("OpenFileGDB 드라이버를 찾을 수 없습니다.");
                        }

                        // GDAL 디버그 정보 활성화
                        Gdal.SetConfigOption("CPL_DEBUG", "ON");
                        Gdal.SetConfigOption("CPL_LOG", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gdal_debug.log"));

                        _isInitialized = true;
                        _logger.LogInformation("GDAL 초기화 완료");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GDAL 초기화 중 오류 발생");
                        
                        // 추가 디버깅 정보
                        _logger.LogDebug("현재 디렉토리: {CurrentDir}", Environment.CurrentDirectory);
                        _logger.LogDebug("앱 기본 경로: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);
                        _logger.LogDebug("PATH 환경 변수: {Path}", Environment.GetEnvironmentVariable("PATH"));
                        _logger.LogDebug("GDAL_DATA: {GdalData}", Environment.GetEnvironmentVariable("GDAL_DATA"));
                        _logger.LogDebug("PROJ_LIB: {ProjLib}", Environment.GetEnvironmentVariable("PROJ_LIB"));
                        _logger.LogDebug("GDAL_DRIVER_PATH: {DriverPath}", Environment.GetEnvironmentVariable("GDAL_DRIVER_PATH"));
                        
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDAL 초기화 실패");
                throw new InvalidOperationException("GDAL 라이브러리를 초기화할 수 없습니다.", ex);
            }
        }

        /// <summary>
        /// PROJ 라이브러리 경로 설정
        /// </summary>
        private void SetupProjLibrary()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Windows DLL 검색 경로 설정
            try
            {
                // 기본 DLL 검색 디렉토리 설정
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
                
                // GDAL 바이너리 경로를 DLL 검색 경로에 추가
                var gdalBinPath = Path.Combine(appDir, "gdal", "bin");
                if (Directory.Exists(gdalBinPath))
                {
                    AddDllDirectory(gdalBinPath);
                    _logger.LogDebug("DLL 검색 경로에 추가: {Path}", gdalBinPath);
                }
                
                // 메인 디렉토리도 추가
                AddDllDirectory(appDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DLL 검색 경로 설정 실패");
            }
            
            // 가능한 PROJ 경로들을 우선순위에 따라 체크
            string[] possibleProjPaths = new[]
            {
                Path.Combine(appDir, "gdal", "share", "proj"),
                Path.Combine(appDir, "gdal", "share"),
                Path.Combine(appDir, "share", "proj"),
                Path.Combine(appDir, "share")
            };

            string projLibPath = null;
            string projDbPath = null;

            // proj.db 파일을 찾을 수 있는 경로 탐색
            foreach (var path in possibleProjPaths)
            {
                if (Directory.Exists(path))
                {
                    var dbPath = Path.Combine(path, "proj.db");
                    if (File.Exists(dbPath))
                    {
                        projLibPath = path;
                        projDbPath = dbPath;
                        _logger.LogInformation("PROJ 데이터베이스 발견: {Path}", dbPath);
                        break;
                    }
                }
            }

            if (projLibPath == null)
            {
                // MaxRev.Gdal.Core는 GdalBase.ConfigureAll()에서 자동으로 PROJ 경로를 설정함
                // 별도 경로가 없어도 정상 작동
                _logger.LogDebug("로컬 PROJ 데이터 경로 없음 - MaxRev.Gdal.Core 기본 설정 사용");
                return; // MaxRev.Gdal.Core의 기본 설정 사용
            }

            var resolvedProjPath = ProjEnvironmentManager.ConfigureFromSharePath(projLibPath, _logger);
            projLibPath = resolvedProjPath;

            // 시스템 PATH에서 PostgreSQL 및 기타 PROJ 경로 제거
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var filteredPaths = paths.Where(p => 
                !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
                !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
                !p.Contains(@"\share\contrib", StringComparison.OrdinalIgnoreCase) &&
                !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase) &&
                !p.Contains(@"Program Files\PostgreSQL", StringComparison.OrdinalIgnoreCase) &&
                !p.Contains(@"Program Files (x86)\PostgreSQL", StringComparison.OrdinalIgnoreCase)
            ).ToArray();
            
            // PROJ 라이브러리 경로를 PATH 최우선순위로 설정
            var cleanPath = string.Join(";", filteredPaths);
            var newPath = projLibPath + ";" + cleanPath;
            Environment.SetEnvironmentVariable("PATH", newPath);
            
            // PROJ 환경 변수 추가 설정
            Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
            Environment.SetEnvironmentVariable("PROJ_DEBUG", "3"); // 디버그 레벨 증가
            Environment.SetEnvironmentVariable("PROJ_USER_WRITABLE_DIRECTORY", projLibPath);
            Environment.SetEnvironmentVariable("PROJ_CACHE_DIR", projLibPath);
            
            // GDAL이 올바른 PROJ를 사용하도록 추가 설정
            Gdal.SetConfigOption("PROJ_SEARCH_PATHS", projLibPath);
            // 추가 PROJ 설정 - 강제로 우리 경로만 사용하도록
            Gdal.SetConfigOption("PROJ_SKIP_READ_USER_WRITABLE_DIRECTORY", "YES");
            Gdal.SetConfigOption("PROJ_IGNORE_USER_WRITABLE_DIRECTORY", "YES");
            
            // PostgreSQL 경로가 있으면 제거
            var postgresPath = @"C:\Program Files\PostgreSQL";
            if (Directory.Exists(postgresPath))
            {
                var postgresqlDirs = Directory.GetDirectories(postgresPath, "*", SearchOption.TopDirectoryOnly);
                foreach (var dir in postgresqlDirs)
                {
                    var projDir = Path.Combine(dir, "share", "contrib", "postgis-3.5", "proj");
                    if (Directory.Exists(projDir))
                    {
                        _logger.LogWarning("PostgreSQL PROJ 경로 발견: {Path}", projDir);
                        // 환경 변수에서 해당 경로 제거
                        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                        path = path.Replace(projDir + ";", "").Replace(";" + projDir, "").Replace(projDir, "");
                        Environment.SetEnvironmentVariable("PATH", path);
                    }
                }
            }
            
            _logger.LogInformation("PROJ 라이브러리 경로 설정 완료: {Path}", projLibPath);
            _logger.LogDebug("현재 PATH: {Path}", Environment.GetEnvironmentVariable("PATH"));
            _logger.LogDebug("PROJ_LIB: {Path}", Environment.GetEnvironmentVariable("PROJ_LIB"));
        }

        /// <summary>
        /// 환경 변수 설정
        /// </summary>
        private void ConfigureEnvironment()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logger.LogDebug("기본 디렉토리: {BaseDir}", baseDir);

                // GDAL 데이터 경로 설정
                var gdalDataPath = Path.Combine(baseDir, "gdal", "data");
                if (!Directory.Exists(gdalDataPath))
                {
                    gdalDataPath = Path.Combine(baseDir, "gdal-data");
                }
                if (Directory.Exists(gdalDataPath))
                {
                    Environment.SetEnvironmentVariable("GDAL_DATA", gdalDataPath);
                    Gdal.SetConfigOption("GDAL_DATA", gdalDataPath);
                    _logger.LogDebug("GDAL_DATA 경로 설정: {Path}", gdalDataPath);
                }

                // GDAL 드라이버 경로 설정 (3.11 이후)
                var driverPath = Path.Combine(baseDir, "gdal", "plugins");
                if (!Directory.Exists(driverPath))
                {
                    driverPath = Path.Combine(baseDir, "gdalplugins");
                }
                if (Directory.Exists(driverPath))
                {
                    Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", driverPath);
                    Gdal.SetConfigOption("GDAL_DRIVER_PATH", driverPath);
                    _logger.LogDebug("GDAL_DRIVER_PATH 설정: {Path}", driverPath);
                }

                // 추가 환경 변수 설정
                Gdal.SetConfigOption("GDAL_FILENAME_IS_UTF8", "YES");
                Gdal.SetConfigOption("SHAPE_ENCODING", "UTF-8");
                Gdal.SetConfigOption("SHAPE_RESTORE_SHX", "YES");
                Gdal.SetConfigOption("GDAL_USE_PROJ", "YES");
                Gdal.SetConfigOption("OSR_USE_ETMERC", "YES");
                Gdal.SetConfigOption("OSR_USE_EPSG_NORTHING_EASTING", "YES");

                // GDAL x64 경로 설정
                var gdalPath = Path.Combine(baseDir, "gdal");
                if (Directory.Exists(gdalPath))
                {
                    var gdalBinPath = Path.Combine(gdalPath, "bin");
                    if (Directory.Exists(gdalBinPath))
                    {
                        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                        if (!currentPath.Contains(gdalBinPath))
                        {
                            Environment.SetEnvironmentVariable("PATH", gdalBinPath + ";" + currentPath);
                            _logger.LogDebug("GDAL bin 경로를 PATH에 추가: {Path}", gdalBinPath);
                        }
                    }
                }

                _logger.LogDebug("환경 변수 설정 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "환경 변수 설정 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// PostgreSQL/PostGIS 등 충돌하는 PROJ 경로를 PATH에서 제거
        /// </summary>
        private void CleanupConflictingProjPaths()
        {
            try
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                
                var filteredPaths = paths.Where(p => 
                    !p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) && 
                    !p.Contains("postgis", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains(@"\share\contrib", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("OSGeo4W", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains(@"Program Files\PostgreSQL", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains(@"Program Files (x86)\PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ).ToArray();
                
                var cleanPath = string.Join(";", filteredPaths);
                Environment.SetEnvironmentVariable("PATH", cleanPath);
                
                // PROJ 관련 환경변수 초기화 (PostgreSQL에서 설정했을 수 있음)
                Environment.SetEnvironmentVariable("PROJ_LIB", null);
                Environment.SetEnvironmentVariable("PROJ_DATA", null);
                
                _logger.LogDebug("충돌하는 PROJ 경로 정리 완료");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PROJ 경로 정리 중 오류 발생");
            }
        }

        /// <summary>
        /// MaxRev.Gdal.Core가 제공하는 실제 PROJ 경로를 찾아 설정
        /// </summary>
        private void SetupMaxRevProjPath()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                
                // MaxRev.Gdal.Core가 제공하는 PROJ 경로들 (우선순위순)
                string[] possibleProjPaths = new[]
                {
                    // MaxRev.Gdal.Core 3.10.x 기본 경로
                    Path.Combine(appDir, "runtimes", "win-x64", "native", "maxrev.gdal.core.libshared"),
                    Path.Combine(appDir, "runtimes", "win-x64", "native"),
                    // 기존 경로 (호환성)
                    Path.Combine(appDir, "gdal", "share", "proj"),
                    Path.Combine(appDir, "gdal", "share"),
                    Path.Combine(appDir, "share", "proj"),
                    Path.Combine(appDir, "share")
                };

                string projLibPath = null;

                foreach (var path in possibleProjPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var dbPath = Path.Combine(path, "proj.db");
                        if (File.Exists(dbPath))
                        {
                            projLibPath = path;
                            _logger.LogInformation("MaxRev PROJ 데이터베이스 발견: {Path}", dbPath);
                            break;
                        }
                    }
                }

                if (projLibPath != null)
                {
                    // 환경변수 설정
                    Environment.SetEnvironmentVariable("PROJ_LIB", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_DATA", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_SEARCH_PATH", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_SEARCH_PATHS", projLibPath);
                    Environment.SetEnvironmentVariable("PROJ_NETWORK", "OFF");
                    
                    // PATH 최우선순위로 추가
                    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!currentPath.StartsWith(projLibPath))
                    {
                        Environment.SetEnvironmentVariable("PATH", projLibPath + ";" + currentPath);
                    }
                    
                    _logger.LogInformation("MaxRev PROJ 경로 설정 완료: {Path}", projLibPath);
                }
                else
                {
                    _logger.LogWarning("MaxRev PROJ 데이터베이스를 찾을 수 없습니다. GdalBase.ConfigureAll()이 자동으로 설정할 것입니다.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MaxRev PROJ 경로 설정 중 오류 발생");
            }
        }
    }
}

