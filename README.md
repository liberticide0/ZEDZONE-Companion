# ZED ZONE Companion

ZED ZONE 플레이 중 아이템, 제작법, 스킬, 지역, 보스를 빠르게 찾는 한국어 Windows 오버레이입니다. 기본 자료와 직접 불러온 자료를 오프라인으로 검색합니다.

## 실행

1. [GitHub Releases](https://github.com/liberticide0/ZEDZONE-Companion/releases)에서 Windows x64 ZIP을 내려받고 압축을 풉니다.
2. `ZEDZONE.Companion.exe`를 실행합니다. 관리자 권한이나 별도 설치 프로그램은 필요하지 않습니다.
3. 게임 창에서 **Ctrl+Shift+Space**로 검색창을 열고 **Esc**로 닫습니다. 시작할 때 검색창이 열려 있으면 먼저 Esc로 숨긴 뒤 게임으로 전환하세요.
4. 기본 단축키가 이미 사용 중이면 **Ctrl+Shift+F10**을 시도합니다. 현재 단축키는 검색창 하단에 표시됩니다. Windows 알림 영역의 Companion 아이콘에서도 검색창을 열거나 프로그램을 종료할 수 있습니다.

검색창에는 한국어·영어·중국어 또는 아이템 ID를 입력할 수 있습니다. 분류를 선택해 결과를 좁힐 수 있습니다. **Ctrl+F**는 검색 입력으로 돌아갑니다. 제작법에서 방사능 폭탄을 선택하고 목표 수량을 입력하면 생산량과 확인된 연료봉 소요량을 볼 수 있습니다.

## 선택 자료

**자료 열기** 또는 **Ctrl+O**로 아래 파일을 하나씩 선택할 수 있습니다. 처음부터 들어 있는 기본 자료는 26개이며 중국어 검색어를 포함합니다.

- `zedzone-localization-v1` 형식의 한국어 현지화 CSV. 검증한 Steam 빌드의 파일 이름은 `Localization_en_to_ko.csv`입니다. 지원되는 텍스트 ID 패턴의 아이템 이름과 설명을 읽습니다.
- 개발사의 영문 `12_Item_ID_Reference.md`. 영어·중국어 이름과 숫자 ID를 읽습니다. [공식 원본 보기](https://raw.githubusercontent.com/IndieDev-LevenLiu/zedzone/c84e688c5df9c9c6cf93ce851a72ebc716ae4d20/mod-docs/docs/en/12_Item_ID_Reference.md)

가져온 자료는 실행 중에만 메모리에 유지됩니다. 가져온 두 표의 ID와 영어 이름이 일치하는 경우 중국어 이름을 한국어 검색 결과에 연결합니다. 버전별로 이름이 다르면 출처를 구분해 표시합니다. 패키지에는 게임 원본과 공식 ID 표가 들어 있지 않습니다.

## 검증 범위

Windows 11 x64와 Steam판 1.0.12 게임 메뉴에서 검증했습니다. 지원 대상에는 Windows 10 x64도 포함되며 해당 OS에서의 실측, 독점 전체 화면, 실제 플레이 중 입력 충돌은 미검증입니다. 오버레이가 보이지 않으면 게임을 창 모드 또는 테두리 없는 창 모드로 실행한 뒤 단축키를 확인하세요.

기본 자료는 검증한 게임 버전 기준의 소규모 안내입니다. 제작 계산은 방사능 폭탄의 확인된 수치만 대상으로 합니다. 지도는 지역 텍스트 안내를 제공하며, 스킬·보스 상세 조건과 누락된 제작 수량은 게임에서 확인하세요.

## 개발

.NET 8 SDK와 Windows x64가 필요합니다.

```powershell
dotnet build src/Companion -c Release
dotnet run --project tests/Smoke -c Release
pwsh -File scripts/package.ps1
```

ZIP과 SHA-256 파일이 `artifacts/`에 생성됩니다. [Phase 0 조사](docs/phase-0.md)와 [검증 결과](docs/qa.md)에 근거와 범위를 기록했습니다.

비공식 팬 도구입니다. ZED ZONE의 권리는 해당 권리자에게 있으며 이 프로젝트의 MIT 라이선스는 Companion 소스에 적용됩니다.
