# ZED ZONE Companion

ZED ZONE 플레이 중 아이템, 제작법, 스킬, 지역, 보스를 빠르게 찾는 한국어 Windows 오버레이입니다. 기본 자료와 직접 불러온 자료를 오프라인으로 검색합니다.

## 실행

1. [GitHub Releases](https://github.com/liberticide0/ZEDZONE-Companion/releases)에서 Windows x64 ZIP을 내려받고 압축을 풉니다.
2. `ZEDZONE.Companion.exe`를 실행합니다. 관리자 권한이나 별도 설치 프로그램은 필요하지 않습니다.
3. 게임 창에서 **Ctrl+Shift+Space**로 검색창을 열고 **Esc**로 닫습니다. 시작할 때 검색창이 열려 있으면 먼저 Esc로 숨긴 뒤 게임으로 전환하세요.
4. 기본 단축키가 이미 사용 중이면 **Ctrl+Shift+F10**을 시도합니다. 현재 단축키는 검색창 하단에 표시됩니다. Windows 알림 영역의 Companion 아이콘에서도 검색창을 열거나 프로그램을 종료할 수 있습니다.

검색창에는 한국어·영어·중국어 또는 아이템 ID를 입력할 수 있습니다. **Ctrl+F**는 검색 입력으로 돌아갑니다. 제작법 자료를 준비한 뒤에는 아이템 상세 또는 제작법 분류에서 작업대를 선택할 수 있습니다. **Ctrl+R**은 제작법 선택으로 이동합니다. 목표 수량을 입력하면 올림한 제작 횟수와 확인된 재료를 표시합니다.

아이템과 제작법은 같은 ID의 검색 결과 하나로 표시됩니다. 상세에서 **목표 저장** 또는 **Ctrl+D**로 원하는 수량을 등록하세요. **찜목록** 또는 **Ctrl+L**에서 합산 재료와 작업대별 제작 순서를 볼 수 있습니다. 중간품의 작업대를 바꾸려면 해당 아이템 상세에서 제작법을 선택하세요. 목표와 선택한 작업대는 Windows 사용자 폴더에 저장됩니다. 같은 작업대 선택은 저장 당시 Steam 빌드에 적용됩니다. 계산은 보유 재고나 소지 도구를 자동 차감하지 않습니다.

오버레이 상단의 앱 이름을 끌어 이동하고, 오른쪽 아래 **작게/크게** 또는 **Ctrl+-**로 크기를 전환합니다. 오른쪽 상단 **×**와 **Esc**는 오버레이를 숨깁니다.

찜목록을 백업하려면 앱을 종료하고 `%LOCALAPPDATA%\ZEDZONE.Companion\wishlist.json`을 복사하세요. 초기화하려면 앱이 꺼진 상태에서 파일을 다른 이름으로 옮겨 보관하세요. 손상된 파일은 앱이 덮어쓰지 않습니다.

## 자료 출처

시작하면 Steam 라이브러리에서 ZED ZONE을 찾아 한국어 현지화 CSV를 읽기 전용으로 불러옵니다. 검색창 하단에 감지한 Steam 빌드와 아이템 후보 수를 표시합니다. 읽을 수 있는 설치가 없으면 내장 자료로 검색합니다.

**자료 열기** 또는 **Ctrl+O**로 아래 파일을 하나씩 직접 선택할 수도 있습니다.

- `zedzone-localization-v1` 형식의 한국어 현지화 CSV. 검증한 Steam 빌드의 파일 이름은 `Localization_en_to_ko.csv`입니다. 지원되는 텍스트 ID 패턴의 아이템 이름과 설명을 읽습니다.
- 개발사의 영문 `12_Item_ID_Reference.md`. 영어·중국어 이름과 숫자 ID를 읽습니다. [공식 원본 보기](https://raw.githubusercontent.com/IndieDev-LevenLiu/zedzone/c84e688c5df9c9c6cf93ce851a72ebc716ae4d20/mod-docs/docs/en/12_Item_ID_Reference.md)

가져온 자료는 실행 중에만 메모리에 유지됩니다. 현지화에서 유도한 ID는 **추정 ID**로 표시합니다. 공식 표의 빌드는 미상이며, ID와 영어 이름이 모두 일치하는 경우 중국어 이름을 연결합니다. 같은 ID의 이름이 다르면 한 결과의 상세에서 출처를 표시하고 제작법 연결에 사용하지 않습니다. 현지화 CSV는 아이템 후보에만 적용됩니다. 패키지에는 게임 원본과 공식 ID 표가 들어 있지 않습니다.

## 설치 파일의 제작법 준비

기본 검색에는 Python이 필요하지 않습니다. 설치된 게임의 정적 제작법을 추가하려면 ZIP의 `probe-recipes.py`를 Python에서 실행합니다. 패키지 설치는 최초 한 번이며, 그 후 변환과 Companion 검색은 로컬에서 처리합니다.

```powershell
python -m pip install UnityPy==1.25.4 TypeTreeGeneratorAPI==0.0.10
python probe-recipes.py "C:\Program Files (x86)\Steam\steamapps\common\ZED ZONE" --snapshot
```

다른 Steam 라이브러리를 쓰면 명령의 게임 폴더를 바꾸세요. 스크립트는 게임을 변경하지 않고 Windows 사용자 데이터 폴더에 빌드별 자료를 만듭니다. 생성 후 알림 영역에서 Companion을 종료했다가 다시 열면 해당 빌드의 자료를 읽습니다. Steam이 업데이트되면 변환을 다시 실행합니다. 작업대별 제작법, 도구·해금 관련 미검증 값과 원본에 기록된 요구 수치를 구분해 표시합니다. Workshop과 실행 중 추가되는 제작법은 범위 밖입니다.

## 검증 범위

Windows 11 x64와 Steam판 1.0.12 게임 메뉴에서 검증했습니다. 지원 대상에는 Windows 10 x64도 포함되며 해당 OS에서의 실측, 독점 전체 화면, 실제 플레이 중 입력 충돌은 미검증입니다. 오버레이가 보이지 않으면 게임을 창 모드 또는 테두리 없는 창 모드로 실행한 뒤 단축키를 확인하세요.

기본 자료는 검증한 게임 버전 기준의 소규모 안내입니다. 설치 파일에서 변환한 제작법 중 의미가 불명확한 값은 부분 계산으로 표시하며, 산출물 ID가 일치하지 않는 제작법은 계산을 보류합니다. 지도는 지역 텍스트 안내를 제공합니다.

## 개발

.NET 8 SDK와 Windows x64가 필요합니다.

```powershell
dotnet build src/Companion -c Release
dotnet run --project tests/Smoke -c Release
pwsh -File scripts/package.ps1
```

ZIP과 SHA-256 파일이 `artifacts/`에 생성됩니다. [Phase 0 조사](docs/phase-0.md)와 [검증 결과](docs/qa.md)에 근거와 범위를 기록했습니다.

비공식 팬 도구입니다. ZED ZONE의 권리는 해당 권리자에게 있으며 이 프로젝트의 MIT 라이선스는 Companion 소스에 적용됩니다.
