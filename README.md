# W Clean

Windows 11용 정리 도구 (C# / .NET 9 / WPF). 구형 알약의 "시작프로그램 · 레지스트리 · 쓰레기파일 · 대용량파일 정리" 기능을 대체합니다.

## 기능

| 탭 | 내용 |
|---|---|
| 시작프로그램 | 레지스트리 Run(HKCU/HKLM/32비트), 시작 폴더, 작업 스케줄러(로그온/부팅) 항목 조회. 사용 / 사용 안 함(작업 관리자와 같은 `StartupApproved` 방식) / 삭제. 가리키는 파일이 없으면 빨간색 표시 |
| 쓰레기파일 정리 | 임시 파일, 휴지통, 썸네일 캐시, Windows 업데이트 캐시, 크래시 덤프, 셰이더 캐시, Chrome/Edge/Whale/Firefox 캐시. 검사 후 체크한 항목만 삭제 |
| 레지스트리 정리 | 삭제된 프로그램의 Uninstall 항목, App Paths, 공유 DLL(Windows 폴더 제외), 프로그램 이름 캐시(MuiCache), 파일 없는 Run 항목 |
| 대용량파일 정리 | 폴더/드라이브에서 지정 크기 이상 파일을 크기순 표시. 휴지통 이동(기본) 또는 영구 삭제. Windows 폴더는 제외 |

## 안전장치

- 레지스트리 삭제 전 항상 복원용 `.reg` 저장 → `%AppData%\WClean\Backups` (더블클릭하면 복원). 작업 스케줄러 항목은 `.xml` 백업.
- 시작 폴더 바로가기와 대용량 파일은 기본적으로 휴지통으로 이동.
- 파일이 없는 것이 확실한 경우만 "문제"로 판단 (연결되지 않은 드라이브, 네트워크 경로, 상대 경로는 건드리지 않음).
- Windows 폴더 안의 파일은 대용량 검색/삭제 대상에서 제외.

## 빌드 / 실행

```powershell
dotnet test tests/WClean.Tests
dotnet run --project src/WClean.App          # Debug: 일반 권한으로 실행
dotnet publish src/WClean.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/WClean-0.1.0-win-x64
```

Release 빌드는 관리자 권한을 요청하는 매니페스트(`app.admin.manifest`)를 사용합니다. HKLM 시작프로그램, 레지스트리, `C:\Windows\Temp` 처리에 필요합니다. Debug 빌드는 `asInvoker`입니다.

## 구조

```
src/WClean.Core   서비스(시작프로그램/쓰레기/레지스트리/대용량), 백업 로직 — UI 없음
src/WClean.App    WPF UI (MVVM)
tests/WClean.Tests
```

## 설치 파일 빌드

Inno Setup 6 필요.

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
```

결과: `artifacts\WClean-0.1.0-win-x64-setup.exe` (한국어/영어, `Program Files\W Clean` 설치, 시작 메뉴 바로가기, 바탕화면 아이콘 선택).
