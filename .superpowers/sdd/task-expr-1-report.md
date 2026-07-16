# Task 1 Report: Tokenizer + AST + Parser

## Files created
- `src/RPA.Infrastructure/Workflow/Expressions/ExpressionErrors.cs`
- `src/RPA.Infrastructure/Workflow/Expressions/ExpressionAst.cs`
- `src/RPA.Infrastructure/Workflow/Expressions/ExpressionToken.cs` (includes `ExpressionTokenizer`)
- `src/RPA.Infrastructure/Workflow/Expressions/ExpressionParser.cs`
- `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ExpressionParserTests.cs`

## Deviation from brief
The brief's types are `internal`. To let the test project (`RPA.Infrastructure.Tests`) see them,
added an `InternalsVisibleTo` entry for `RPA.Infrastructure.Tests` to
`src/RPA.Infrastructure/RPA.Infrastructure.csproj` (no such attribute existed previously). This was
required — without it, the brief's own test file fails to compile (CS0122, protection level).

## TDD Evidence

### RED (test file present, types missing)
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionParser
...
error CS0234: 'Expressions' tür veya ad alanı adı 'RPA.Infrastructure.Workflow' ad alanında yok
```
(compile failure — namespace/types not yet created)

### GREEN (after implementation + InternalsVisibleTo)
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionParser
...
Başarılı!  - Başarısız:     0, Başarılı:    10, Atlanan:     0, Toplam:    10, Süre: 90 ms - RPA.Infrastructure.Tests.dll (net10.0)
```
All 10 tests (7 Fact + 4 Theory InlineData under InvalidSyntax_ThrowsBusiness... actually 6 Fact + 4 Theory = 10) pass.

## Build
`dotnet build src/RPA.Infrastructure` — 0 warnings, 0 errors (new files only).

## Commit
See git log for exact SHA/subject (commit message per brief Step 8, adjusted body to note the
InternalsVisibleTo addition).
