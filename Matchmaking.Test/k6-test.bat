@echo off
setlocal

REM ============================================================
REM  k6 yuk testi - Matchmaking API
REM  loadtest.js icindeki handleSummary, sonuclari
REM  loadtest-results klasorune yazar.
REM ============================================================

REM Proje koku (bu .bat dosyasinin bir ustu)
cd /d "%~dp0.."

if not exist "loadtest-results" mkdir "loadtest-results"

where k6 >nul 2>&1
if errorlevel 1 (
    echo [HATA] k6 PATH'te bulunamadi. Terminali yeniden baslatmayi deneyin.
    exit /b 1
)

echo ============================================================
echo  k6 run Matchmaking.Test\loadtest.js
echo ============================================================
k6 run "Matchmaking.Test\loadtest.js"

echo.
echo ============================================================
echo  Bitti. Sonuclar:
echo    loadtest-results\k6-result.txt
echo    loadtest-results\k6-summary.json
echo ============================================================
endlocal
