# 업데이트 팝업 테스트

일반 사용 빌드는 `artifacts/ZEDZONE-Companion-0.4.8-win-x64.zip`이다. 아래 테스트 두 파일은 `artifacts/update-popup-fixtures`에 있으며 일반 사용 빌드와 별개다.

| 역할 | 파일 |
| --- | --- |
| 시작 | `ZEDZONE-Companion-0.4.8-win-x64.zip` |
| 업데이트 대상 | `ZEDZONE-Companion-0.4.9-win-x64.zip` |

PowerShell 7에서 저장소로 이동한 뒤 실행한다.

```powershell
./scripts/start-update-test.ps1 -Delay 0.2
```

1. 제목이 **ZED ZONE Companion · 업데이트 검증**인 창이 열린다. 실행 폴더는 터미널에 표시된다. **찜목록**을 열면 CNC 부품 목표를 볼 수 있다. 해당 빌드의 변환 자료가 이 PC에 있으면 테스트 폴더의 사본으로 작업대와 재료 체크도 볼 수 있다.
2. **업데이트 확인**을 누른다. 창 안에 현재 v0.4.8과 새 v0.4.9가 표시된다. 첫 Esc 또는 **나중에**를 누르면 팝업만 닫힌다.
3. 다시 **업데이트 확인 → 업데이트 후 다시 시작**을 누른다. 검증 창이 닫힌 뒤 같은 위치에서 자동으로 열린다. **업데이트 확인**을 다시 누르면 현재 v0.4.9와 **새 업데이트 없음**이 표시된다. 찜목록에서 목표와 재료 체크를 확인할 수 있다.
4. 끝나면 검증 앱을 알림 영역에서 종료하고 터미널에서 Ctrl+C를 누른다.

오류를 시험하려면 검증 앱과 기존 테스트 서버를 종료한 뒤 `./scripts/start-update-test.ps1 -Mode bad-hash` 또는 `-Mode unavailable`로 새 세션을 시작한다. `bad-hash`에서는 설치를 선택해도 파일을 교체하지 않고 해시 오류 카드를 표시한다. `unavailable`에서는 조회 오류 카드를 표시한다.

검증 빌드는 이 PC의 루프백 피드에만 연결하고, 사용자 자료는 검증 폴더 아래에서 따로 사용한다. 일반 사용 빌드는 공개 GitHub 정식 릴리스를 조회한다. 검증용 ZIP 두 개는 정식 Release에 첨부하지 않는다. 이전의 `artifacts/update-fixtures` 아래 v0.4.7은 이전 교체 검증에서 사용한 별도 파일이다.
