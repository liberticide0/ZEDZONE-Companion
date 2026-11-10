# Phase 0 조사 · 2026-11-09

## 환경과 데이터

Windows 11 x64에서 프로젝트와 Steam 설치 경로에 접근했다. 프로젝트는 공개 저장소이며 초기 `main`은 원격과 같은 `cb1a2bc`였다. 초기 문서는 제목만 있는 README와 MIT 라이선스였다. 게임은 Steam 빌드 **26528677**, 실행 화면의 버전은 **1.0.12**였다.

| 범위 | 관측 | 공개 빌드에 반영 |
|---|---|---|
| 아이템 · 한국어/영어 | `ZEDZONE_Data/StreamingAssets/Localization`에 현지화 CSV 8개가 있다. 한국어 CSV에는 고유한 숫자 텍스트 ID 6,562개가 있다. 관측된 아이템 이름 후보 712개에 설명 ID가 대응했다. | 일부 이름과 사실을 수기로 정리한다. 사용자가 고른 로컬 표는 메모리에서 읽는다. 관측한 ID 관계는 새 버전에서 재검증한다. |
| 중국어 | 공식 MOD 문서에 고유한 아이템 ID 543개와 영어·중국어 이름이 있다. 로컬 현지화 폴더에는 별도 중국어 CSV가 없었다. 문서와 로컬 빌드 간 이름 차이가 있어 ID만으로 일괄 결합하기 어렵다. | 기본 자료에는 자체 중국어 검색어를 넣는다. 공식 ID 표는 사용자가 별도로 선택할 수 있게 한다. |
| 제작법 | 공식 문서가 MOD 제작 재료, 결과 수량, 작업대, 도구, 능력치 필드를 설명한다. 로컬 현지화 표에서 개별 제작 단서를 확인했다. 완전한 원본 제작법 표는 확인하지 못했다. | 확인한 작업대와 재료만 표시한다. 확인된 투입량과 산출량이 있으면 배치 계산에 사용한다. |
| 스킬·지도·보스 | CSV에서 능력 이름과 설명, 지명, 입장 안내, 일부 수호자와 보상 정보를 확인했다. 게임 맵 이미지, 좌표, 전체 해금 그래프, 보스 스탯의 구조화된 출처는 확인하지 못했다. | 텍스트로 정리한 소규모 기본 자료를 제공한다. |
| Unity 직렬화 | `resources.assets`와 `globalgamemanagers.assets` 헤더는 Unity **2023.1.18f1**이다. `GameAssembly.dll`과 `il2cpp_data/Metadata/global-metadata.dat`가 있다. UnityPy로 `resources.assets`의 MonoBehaviour 9,552개를 열거했고 내장 TypeTree가 있는 항목은 없었다. `globalgamemanagers.assets`에서 제작·스킬·보스 관련 스크립트 이름은 확인했으나 스키마와 값의 완전한 관계는 확인하지 못했다. | Unity 자산 조사에서 메타데이터를 읽는 단계까지만 수행했다. 직렬화 구조를 추정해 자동 수집한 값은 배포에 사용하지 않는다. |

개발사 문서로 MOD 에디터의 원본 아이템 템플릿 가져오기와 MOD 공유 흐름을 확인했다. 공개 Workshop에서 실제 아이템·묶음 사례도 확인했다. 에디터에서 직접 내보내기, 전체 바닐라 레시피 추출, MOD 설치는 검증 범위에 포함하지 않았다. 개발사 자료에는 별도 라이선스가 표시되지 않았고, 조사한 외부 위키 자료에도 패키지 재배포 근거를 확보하지 못했다. 배포물에는 자체 요약과 코드만 담고, 게임 파일·원본 표·지도·이미지는 사용자 기기에 둔다.

- [공식 MOD 및 현지화 문서](https://github.com/IndieDev-LevenLiu/zedzone/tree/c84e688c5df9c9c6cf93ce851a72ebc716ae4d20)
- [공식 Steam 업데이트](https://steamcommunity.com/app/1211600/allnews/) · [Workshop](https://steamcommunity.com/app/1211600/workshop/) · [커뮤니티 가이드](https://steamcommunity.com/app/1211600/guides/)
- [커뮤니티 데이터베이스](https://zedzone.wiki/database/) · [Wikily](https://wikily.gg/zed-zone) · [Fandom](https://zed-zone.fandom.com/wiki/Zed_Zone_Wiki)

## 오버레이와 배포 기술

| 후보 | Windows에서의 조건 |
|---|---|
| Tauri | 시스템 WebView2와 Rust 툴체인을 사용한다. 웹 UI에 적합하며 포터블 배포 대상에서 WebView2 가용성을 고려해야 한다. |
| Electron | Chromium·Node 기반 웹 UI 환경을 함께 배포한다. 브라우저 엔진과 여러 프로세스의 동작도 관리 대상에 포함된다. |
| WinForms + Win32 | 이미 있는 .NET SDK로 Windows 전용 UI를 빌드하고, 필요한 런타임을 단일 파일에 포함할 수 있다. 전역 단축키 등록과 창 포커스를 OS API로 직접 다룬다. |

**결정**은 WinForms와 Win32다. 게임과 별도인 최상위 창을 사용한다. 단축키 충돌 시 보조 단축키와 알림 영역을 제공한다. 포커스 복귀는 Windows가 허용하는 범위에서 이전 창으로 요청한다. 독점 전체 화면과 다양한 디스플레이 배율, 서로 다른 권한으로 실행한 환경은 별도 실측 항목이다.

- [Tauri 구조](https://v2.tauri.app/concept/architecture/) · [Electron 프로세스 모델](https://www.electronjs.org/docs/latest/tutorial/process-model) · [.NET 단일 파일 배포](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- [RegisterHotKey](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey) · [SetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow)

## Phase 1–4 범위

별도 창의 단축키·포커스는 실제 게임 메뉴에서 검증한다. 이후 출처가 표시되는 오프라인 검색, 소규모 제작 계산 및 지역·보스 안내, 자체 포함형 Windows x64 ZIP을 순서대로 완성한다. 전체 카탈로그의 자동 추출과 재배포 권한은 미확보다. 포터블 MVP 진행에는 중대한 블로커가 없다. Steam 업데이트 이후에는 UI 버전과 Steam 빌드를 기록하고, CSV 형식·아이템 ID·개별 수량을 다시 대조한 뒤 기본 자료를 새 릴리스에 반영한다.

## 계획 v1.2 재평가

Steam 앱 1211600의 설치 빌드 26528677과 게임 메뉴의 1.0.12를 다시 확인했다. 같은 빌드의 현지화 후보 712개와 고정 리비전의 공식 ID 표 543개를 읽기 전용으로 대조했다. ID가 겹친 543건 중 정규화한 영문명이 같은 것은 507건이었고, 36건은 영문명이 다르거나 비어 있었다. 이는 두 파일의 텍스트 비교 결과이며 실제 게임 전체 수나 모든 항목의 동일성 판정으로 쓰지 않는다. 공식 표의 게임 빌드는 확인되지 않았다.

v0.2.0은 Steam 등록 경로와 라이브러리 목록을 따라 앱 매니페스트와 한국어 CSV를 읽는다. 원본은 사용자 기기에 두고 후보를 메모리에만 적재한다. 로컬 표와 공식 ID 표를 함께 쓸 때 동일 ID의 영문명이 다르면 결과를 분리한다. 중국어 공식 표는 수동 선택 자료다.

현지화 텍스트의 장전 매뉴얼 안내에서 60mm 고폭 유탄과 84mm 고폭탄의 작업대와 해금 조건을 확인했다. 재료·수량은 해당 안내에 없다. 방사능 폭탄은 확인된 연료봉만 부분 계산한다. 전체 제작법의 원본 스키마·수치와 배포 권한은 여전히 미확보다. 이 제한은 전체 제작 데이터 완료 기준에 영향을 주며, 로컬 아이템 검색 확대와 확인된 텍스트 안내는 독립적으로 진행했다.

## 기본 게임 제작법 일괄 조회 확인

Steam 빌드 26528677의 `globalgamemanagers` 리소스 인덱스에서 `properties/item/<숫자 ID>`의 경로를 열거했다. UnityPy와 TypeTreeGeneratorAPI의 AssetStudio 백엔드로 로컬 Unity 2023.1.18f1 IL2CPP 메타데이터에서 읽기 스키마를 구성하고 `resources.assets`의 아이템 제작법 배열을 읽었다. 717개 항목을 ID 중복이나 역직렬화 실패 없이 처리했다. 현지화 후보 712개 중 711개 ID가 교집합이었고, 자산에만 있는 ID는 6개, 현지화 후보에만 있는 ID는 1개다. Workshop 추가 아이템과 런타임 변경은 이 집계 범위 밖이다.

- 제작법 배열이 있는 아이템 자산 **312개**, 작업대·대체 조합을 각각 센 정적 제작법 **375건**. 제작법이 있는 항목 중 `unlockByDefault=false`인 자산은 **99개**다. 이 플래그만으로 개별 해금 조건이나 현재 플레이에서의 표시 여부를 확정할 수 없다.
- 12 게이지의 손 제작 3발과 탄약 압착기 6발은 별도 레시피다. 손 제작의 탄피 6개, 화약 1개, 레더맨 멀티툴 1개는 앞서 게임 상세 화면에서 확인한 값과 일치한다. 멀티툴은 자산에 음수 수량으로 기록돼 있다. 모든 음수 수량의 의미는 별도 검증 대상이다.
- 투입 ID는 모두 읽어 낸 자산 ID에 대응했다. 분수 수량 17건과 음수 수량 15건이 있으며, 제작 결과 ID가 레시피를 소유한 아이템 ID와 다른 경우도 34건이다. 다중 제작법, 소모 재료와 도구, 실제 생산품의 관계를 확인한 뒤 계산기에 연결해야 한다.

개발 PC에서 Python에 `UnityPy==1.25.4`와 `TypeTreeGeneratorAPI==0.0.10`을 준비한 뒤 `python scripts/probe-recipes.py "<게임 설치 폴더>"`로 집계를 재현할 수 있다. `--item 100`은 해당 아이템만, `--all`은 정적 제작법 전체를 로컬 콘솔에 표시한다. 이 도구는 게임을 시작하거나 파일을 수정하지 않고, 데이터 덤프 파일도 생성하지 않는다. 조회 기능은 v0.2.0 앱이나 ZIP에 아직 연결하지 않았다.

[UnityPy](https://github.com/K0lb3/UnityPy) · [TypeTreeGenerator](https://github.com/K0lb3/TypeTreeGenerator) · [개발사 제작 데이터 구조](https://raw.githubusercontent.com/IndieDev-LevenLiu/zedzone/c84e688c5df9c9c6cf93ce851a72ebc716ae4d20/mod-docs/docs/en/13_Authoring_Extras.md#recipedata--recipeitemdata-structure)
