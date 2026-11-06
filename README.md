# W Clean

Windows 11용 정리 도구 (C# / .NET 9 / WPF). 구형 알약의 "시작프로그램 · 레지스트리 · 쓰레기파일 · 대용량파일 정리" 기능을 대체합니다.

## 라이선스

MIT (`LICENSE`). 사용한 오픈소스는 `THIRD-PARTY-NOTICES.md` 참고.

## 기능

| 탭 | 내용 |
|---|---|
| 시작프로그램 | 레지스트리 Run(HKCU/HKLM/32비트), 시작 폴더, 작업 스케줄러(로그온/부팅) 항목 조회. 사용 / 사용 안 함(작업 관리자와 같은 `StartupApproved` 방식) / 삭제. 가리키는 파일이 없으면 빨간색 표시. 부팅 영향(Windows가 기록한 시작 지연, 관리자 권한 필요)과 마지막 부팅 시간 |
| 쓰레기파일 정리 | 임시 파일, 휴지통, 썸네일 캐시, Windows 업데이트·배달 최적화 캐시, 크래시 덤프, 셰이더 캐시, Chrome/Edge/Whale/Firefox 캐시, Discord·Slack·Teams·VS Code 캐시, 개발 도구(NuGet/npm/pip) 캐시. 검사 후 체크한 항목만 삭제 |
| 레지스트리 정리 | 삭제된 프로그램의 Uninstall 항목, App Paths, 공유 DLL(Windows 폴더 제외), 프로그램 이름 캐시(MuiCache), 파일 없는 Run 항목 |
| 대용량파일 정리 | 폴더/드라이브에서 지정 크기 이상 파일을 크기순 표시. 휴지통 이동(기본) 또는 영구 삭제. Windows 폴더는 제외 |
| 중복파일 찾기 | 크기 → 일부 해시 → 전체 SHA-256 순으로 내용이 같은 파일 묶음을 찾음. ‘중복 자동 선택’은 묶음마다 가장 오래된 파일 하나를 남김. 모든 사본을 지우는 것은 막음. 휴지통으로 이동 |
| 디스크 사용량 | 폴더별 용량을 크기에 비례한 트리맵으로 표시. 두 번 클릭하면 폴더 안으로 이동 |
| 프로그램 제거 | 설치된 프로그램을 용량·설치일·이름순으로 보고 제거(프로그램 자체 제거 마법사 실행). 제거 후 남은 설치 폴더를 휴지통으로 이동할지 물어봄 |
| 서비스 관리 | 자동 시작 서비스의 시작 유형(자동/수동/사용 안 함) 변경, 시작/중지. 기본으로 Microsoft 서비스는 숨김 |
| 빈 폴더·바로가기 | 대상이 사라진 바로가기(바탕화면·시작 메뉴)와 빈 폴더 정리 |
| 백업 복원 | 레지스트리/작업 스케줄러 삭제 전에 저장된 백업을 앱에서 복원하거나 삭제 |
| 기록 · 설정 | 누적 정리 용량과 내역, 자동 정리 예약(매일/매주/매월, 휴지통 제외), GitHub 릴리즈 업데이트 확인(알림만, 자동 설치 안 함) |

## 안전장치

- 레지스트리 삭제 전 항상 복원용 `.reg` 저장 → `%AppData%\WClean\Backups` (더블클릭하면 복원). 작업 스케줄러 항목은 `.xml` 백업.
- 시작 폴더 바로가기와 대용량 파일은 기본적으로 휴지통으로 이동.
- 파일이 없는 것이 확실한 경우만 "문제"로 판단 (연결되지 않은 드라이브, 네트워크 경로, 상대 경로는 건드리지 않음).
- Windows 폴더 안의 파일은 대용량 검색/삭제 대상에서 제외.
- 네트워크 사용은 업데이트 확인(GitHub API 조회)뿐이며 `기록 · 설정`에서 끌 수 있습니다. 파일을 내려받거나 설치하지 않습니다.

## 빌드 / 실행

```powershell
dotnet test tests/WClean.Tests
dotnet run --project src/WClean.App          # Debug: 일반 권한으로 실행
dotnet publish src/WClean.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/WClean-0.2.0-win-x64
```

Release 빌드는 관리자 권한을 요청하는 매니페스트(`app.admin.manifest`)를 사용합니다. HKLM 시작프로그램, 레지스트리, `C:\Windows\Temp` 처리에 필요합니다. Debug 빌드는 `asInvoker`입니다.

## 구조

```
src/WClean.Core   서비스(시작프로그램/쓰레기/레지스트리/대용량/중복/디스크/제거/서비스 등), 백업 로직 — UI 없음
src/WClean.App    WPF UI (MVVM)
tests/WClean.Tests
```

## 설치 파일 빌드

Inno Setup 6 필요.

```powershell
.\scripts\build-installer.ps1 -Version 0.2.0
```

결과: `artifacts\WClean-0.2.0-win-x64-setup.exe` (한국어/영어, `Program Files\W Clean` 설치, 시작 메뉴 바로가기, 바탕화면 아이콘 선택).
