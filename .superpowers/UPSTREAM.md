# Superpowers Vendor Information

FileGateway는 Superpowers를 Git submodule이 아닌 프로젝트 내부 일반 파일로 포함합니다.

- Upstream: https://github.com/obra/superpowers
- Release baseline: v6.3.0
- Upstream commit: `b36e0829c6d0140e93cfef2ca599b1b07d4a7797`
- License: MIT (`LICENSE` 참조)

## 포함 범위

이 저장소에서 사용하는 프로젝트 레벨 runtime skills와 해당 skills가 실행 시 참조하는 핵심 지원 파일을 `.superpowers/skills`에 vendor합니다. upstream 저장소의 테스트 fixture, release tooling, 다른 harness용 플러그인 전체를 복제하지는 않습니다.

## 프로젝트 로컬 조정

- `systematic-debugging/SKILL.md`: upstream의 환경변수/secret 값을 출력하는 진단 예제는 제외했습니다. root-cause-first 디버깅 원칙과 단계는 유지합니다.
- `using-superpowers/SKILL.md`: FileGateway에서 사용하는 Claude/Codex/OpenCode/OMP 프로젝트 로컬 구조에 맞게 간소화했습니다.
- `writing-skills/SKILL.md`: 프로젝트 사용에 필요한 핵심 원칙만 남긴 self-contained 버전으로 정리했습니다.

그 외 포함된 runtime 파일은 가능한 한 v6.3.0 기준 upstream 내용을 유지합니다.

## 업데이트

Superpowers 버전 갱신 시 새 upstream 릴리스를 검토한 뒤 `.superpowers/skills`의 vendored 파일을 갱신하고, 이 문서의 release/commit 값을 함께 변경합니다.

Claude/Codex/OpenCode/OMP의 프로젝트 skill 경로는 `.superpowers/skills`를 가리키므로 버전 갱신 시 각 링크를 변경할 필요가 없습니다.
