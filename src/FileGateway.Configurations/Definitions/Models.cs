// src/FileGateway.Configurations/Definitions/Models.cs
using FileGateway.Core.Files;

namespace FileGateway.Configurations.Definitions;

/// <summary>세그먼트 종류. persisted 표현은 기존 PathTemplate 문자열이며 `/` 세그먼트 문법만 확장한다(설계 §1.1).</summary>
public enum PathSegmentKind { Literal, DateFormat, Regex }

/// <summary>Literal = token 없는 template 세그먼트, DateFormat = 날짜 token ≥1개 포함 세그먼트,
/// Regex = `regex:PATTERN` 세그먼트(Value = prefix 제거한 pattern). 평가 동작은 Literal/DateFormat이 동일하다.</summary>
public sealed record PathSegment(PathSegmentKind Kind, string Value);

/// <summary>파일명 매칭 모드. mode 컬럼 미지정(빈 값)은 Glob 기본값(설계 §2.1).</summary>
public enum FileMatchMode { Literal, Glob, Regex }

/// <summary>metadata 추출 모드. Logs의 MetadataMode와 의미는 같지만 이름 충돌로 별도 타입이다
/// (ReferenceDataSnapshotBuilder가 두 Definitions 네임스페이스를 동시 import — 설계 §3.2, P2-3).</summary>
public enum ConfigurationMetadataMode { Template, Regex }

/// <summary>Logs의 MetadataMapping과 동일 JSON 형태(group/target/format)를 담는 Configuration 측 매핑.</summary>
public sealed record ConfigurationMetadataMapping(string Group, string Target, string? Format);

/// <summary>Configuration metadata rule. Regex 모드는 mapping 정확히 1개(단일 ts named group),
/// Template 모드는 mappings가 빈 목록(토큰 기반 파생)이다(설계 §3.2).</summary>
public sealed record ConfigurationMetadataRule(
    ConfigurationMetadataMode Mode,
    string Pattern,
    IReadOnlyList<ConfigurationMetadataMapping> Mappings);

/// <summary>Current 탐색 규칙. FileMatchMode는 persisted 모드 문자열(빈 값 = Glob 기본)로,
/// 구조화·컴파일은 ConfigurationRuleParser가 담당한다.</summary>
public sealed record CurrentRule(string PathTemplate, string FilePattern, string FileMatchMode = "");

/// <summary>History 탐색 규칙. Metadata는 정의에 rule이 없으면 null(→ snapshotTimestamp = 날짜 폴더 자정).</summary>
public sealed record HistoryRule(
    string PathTemplate,
    string FilePattern,
    string MarkerPathTemplate,
    string FileMatchMode = "",
    ConfigurationMetadataRule? Metadata = null);

public sealed record EquipmentConfigurationDefinition(
    string EquipmentId,
    string ConfigurationType,
    string ServerId,
    CurrentRule CurrentRule,
    HistoryRule HistoryRule);

public sealed record ResolvedConfigurationDefinition(EquipmentConfigurationDefinition Definition, FileServerConnection Server);
