# Windows 포터블 릴리스

## 로컬 개발과 사용자 검증

기능 개선과 버그 수정은 현재 공개 버전 번호를 유지한 채 로컬에서 빌드하고 검증한다. 로컬 검증본은 배포본과 다른 경로에 보관한다. 사용자 테스트 중 받은 추가 요청도 같은 개발 작업에 반영한다.

## 출하

사용자가 테스트를 마치고 **“출하해”**라고 요청하면 버전을 올린다. 변경 사항을 `main`에 반영하고 GitHub 정식 릴리스에 포터블 ZIP과 SHA-256을 게시한 뒤, 이전 공개 버전에서 업데이트 조회와 다운로드를 확인하면 출하가 완료된다.

1. 배포할 커밋의 `Companion.csproj`에서 세 자리 버전 `X.Y.Z`를 지정한다. 그 커밋에서 `dotnet run --project tests/Smoke -c Release`와 `./scripts/package.ps1`을 실행한다. 스크립트는 프로젝트 버전을 읽고 실행 파일의 제품 버전과 일치하는지 확인한다.
2. Windows에서 ZIP을 풀고 실행한다. ZIP에는 실행 파일, 자체 스크립트, 변환 환경과 고지만 포함한다. 게임 원본, 추출 자료, 사용자 파일 및 별도 파서 다운로드 파일은 제외한다.
3. 기존 업데이트 지원 버전과 새 패키지를 별도 경로에서 시험한다. 다른 Companion 인스턴스를 닫고 `dotnet run --project tests/Smoke -c Release -- --update-integration <이전 ZIP> <새 ZIP>`을 실행한다. 초기 검증 쌍은 v0.4.6과 테스트 전용 v0.4.7이다. 네트워크 오류, 잘못된 해시, 권한 제한, 중단 복구와 같은 위치에서 재실행을 확인한다.
4. 공개할 때는 검토를 마친 동일 커밋에 `vX.Y.Z` 태그를 지정하고 GitHub에 초안 릴리스를 만든다. `ZEDZONE-Companion-X.Y.Z-win-x64.zip`과 `.zip.sha256`을 자산으로 첨부한다. 게시 전 ZIP 해시, GitHub가 계산한 자산 해시, 태그·ZIP 이름·실행 파일 버전을 비교한다. 먼저 게시한 뒤 자산을 추가하는 방식은 확인 중인 사용자에게 불완전한 릴리스를 노출할 수 있다.
5. 정식 릴리스를 최신 릴리스로 게시한다. 공개 API에서 `draft=false`, `prerelease=false`, 자산 `state=uploaded`, 올바른 `digest`를 확인한다. 이전 공개 버전 기준으로 앱의 업데이트 조회 코드가 새 버전을 찾고, 인증 없이 ZIP을 내려받아 해시를 검증하는지 확인한다. 새 버전 안내에는 업데이트 기능 이전 버전 사용자가 새 ZIP을 한 번 직접 풀어 실행해야 한다고 명시한다.

업데이트는 지정된 공개 저장소의 정식 릴리스 응답과 HTTPS 다운로드를 신뢰한다. 앱은 그 응답의 SHA-256으로 전송된 파일을 검증한다. 작업 중에는 설치 폴더의 배포 구성만 교체하며, 실행 파일을 마지막에 바꿔 중단된 교체를 복구할 수 있도록 한다.

팝업을 직접 검증할 때는 `./scripts/package.ps1 -FixtureVersion X.Y.Z`로 시작·대상 ZIP을 만든다. 이 빌드에는 루프백 피드와 검증용 데이터 경로가 컴파일되며 `artifacts/update-popup-fixtures`에 저장된다. 정식 배포 ZIP은 옵션 없이 빌드한다. [팝업 실행 절차](update-popup-test.md)

[GitHub 정식 릴리스 API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
