@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  Bombardier yuk testi - Matchmaking API
REM  queue (POST) ve leaderboard (GET) endpointlerini test eder
REM ============================================================

REM --- Ayarlar ---
set "BOMBARDIER=%USERPROFILE%\Desktop\bombardier.exe"
set "BASE_URL=http://localhost:8080"
set "CONNECTIONS=50"
set "REQUESTS=1000"

REM Sonuc klasoru (proje kokunde loadtest-results)
set "OUTDIR=%~dp0..\loadtest-results"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

REM bombardier var mi kontrolu
if not exist "%BOMBARDIER%" (
    echo [HATA] bombardier bulunamadi: %BOMBARDIER%
    echo BOMBARDIER degiskenini bu dosyada duzenleyin.
    exit /b 1
)

echo ============================================================
echo  TEST 1: POST %BASE_URL%/api/matchmaking/queue
echo  %CONNECTIONS% baglanti, %REQUESTS% istek
echo ============================================================
> "%OUTDIR%\queue-result.txt" echo === TEST 1: POST /api/matchmaking/queue (%CONNECTIONS% conn, %REQUESTS% istek) ===
"%BOMBARDIER%" -c %CONNECTIONS% -n %REQUESTS% -m POST ^
    -H "Content-Type: application/json" ^
    -b "{\"userId\":\"loadtest-user\",\"score\":1500}" ^
    --print=intro,result ^
    "%BASE_URL%/api/matchmaking/queue" >> "%OUTDIR%\queue-result.txt" 2>&1
type "%OUTDIR%\queue-result.txt"

echo.
echo ============================================================
echo  TEST 2: GET %BASE_URL%/api/matchmaking/leaderboard
echo  %CONNECTIONS% baglanti, %REQUESTS% istek
echo ============================================================
> "%OUTDIR%\leaderboard-result.txt" echo === TEST 2: GET /api/matchmaking/leaderboard (%CONNECTIONS% conn, %REQUESTS% istek) ===
"%BOMBARDIER%" -c %CONNECTIONS% -n %REQUESTS% -m GET ^
    --print=intro,result ^
    "%BASE_URL%/api/matchmaking/leaderboard" >> "%OUTDIR%\leaderboard-result.txt" 2>&1
type "%OUTDIR%\leaderboard-result.txt"

echo.
echo ============================================================
echo  Bitti. Sonuclar: %OUTDIR%
echo ============================================================
endlocal
