# Task 12 Report: Configurations — Current resolver + fileId

## Status

Implementation complete. Commit subject: `feat(configurations): current set resolution and file ids`

## Implemented

- Added `CurrentResolver` and `ResolvedConfigFile`.
  - Lists the configured current directory through `IFileAccess`.
  - Treats a missing directory as an empty result.
  - Applies the configured glob pattern.
  - Rejects case-insensitive duplicate matching names with `FileDefinitionConflict`.
  - Sorts results by case-insensitive `fileName ASC`.
- Added `ConfigurationItem`.
- Added the complete `IConfigurationQueryService` contract from the brief.
- Added the currently required `ConfigurationHistoryItem` and `ConfigurationHistoryQuery` contract records in `ConfigurationItems.cs`; Task 13 should reuse these records when adding history behavior.
- Added the four requested `CurrentResolverTests`.

## RED evidence

Command:

```bash
dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~CurrentResolverTests"
```

Result before the production seam existed: exit code `1`. Existing projects built, then compilation failed with:

```text
CurrentResolverTests.cs(2,34): error CS0234: 'FileGateway.Configurations' 네임스페이스에 'Internal' 형식 또는 네임스페이스 이름이 없습니다.
```

After the resolver was added, the unmodified brief fixture still produced 3 failures/1 pass because `PM_*.cfg` cannot match the brief's `PM1.cfg` and `pm2.cfg` names under the existing anchored glob implementation. After changing only the test fixture pattern to `PM*.cfg`, 3 tests passed and the duplicate test exposed the existing `FakeFileAccess` case-insensitive dictionary limitation. The duplicate scenario was then supplied by a local listing-only fake, matching the established `LogResolverTests` approach.

## GREEN and required gates

Focused resolver tests:

```text
dotnet test tests/FileGateway.UnitTests --filter "FullyQualifiedName~CurrentResolverTests"
exit code: 0
실패: 0, 통과: 4, 건너뜀: 0, 전체: 4
```

Full build:

```text
dotnet build
exit code: 0
경고 0개
오류 0개
```

Full unit suite:

```text
dotnet test tests/FileGateway.UnitTests
exit code: 0
실패: 0, 통과: 152, 건너뜀: 0, 전체: 152
```

No integration tests were run, as requested.

## Files changed

- `src/FileGateway.Configurations/Internal/CurrentResolver.cs`
- `src/FileGateway.Configurations/ConfigurationItems.cs`
- `src/FileGateway.Configurations/IConfigurationQueryService.cs`
- `tests/FileGateway.UnitTests/Configurations/CurrentResolverTests.cs`
- `.superpowers/sdd/2026-08-23-filegateway-mvp/task-12-report.md`

## Self-review

- The resolver matches the brief's requested implementation and uses the existing public Core seams.
- The cached diff contains only the intended Task 12 source/test files before this report was added.
- `git diff --cached --check` was clean.
- No `ConfigurationQueryService`, history resolver, pagination, or integration behavior was added.
- The fixture-only `PM*.cfg` correction is necessary because the brief's `PM_*.cfg` pattern contradicts its `PM1.cfg`/`pm2.cfg` test data.
- The local duplicate-listing fake is necessary because the supplied shared `FakeFileAccess` stores paths with a case-insensitive dictionary and cannot represent two case-only variants.

## Concerns

The Task 12 brief requires the complete interface while the current repository does not yet contain `ConfigurationHistoryItem` or `ConfigurationHistoryQuery`, which Task 13 owns. To keep the required interface compilable without creating `ConfigurationQueryService` or changing another file, both future-facing records are declared in `ConfigurationItems.cs`. Task 13 should reuse them rather than redeclare duplicate types (and may move `ConfigurationHistoryQuery` to its planned file if desired).
