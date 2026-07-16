# Task 2 Report: AST evaluator + FunctionRegistry iskeleti + ExpressionEvaluator delegasyonu

## Summary

Implemented `ExpressionEngine` (AST evaluator) + `FunctionRegistry` skeleton (with empty
`DateFunctions`/`StringFunctions`/`ConversionFunctions`/`HelperFunctions` modules for Tasks 3-5)
and rewired `ExpressionEvaluator` to delegate token-content resolution to the new engine, per the
brief in `.superpowers/sdd/task-2-brief.md`. Public API of `ExpressionEvaluator`
(`EvaluateValue`/`EvaluateString`/`EvaluateCondition`) is unchanged; the `${}`/`{{}}`/template/
condition regex layer is untouched. Old private `ResolvePath` removed from `ExpressionEvaluator`
(moved into `ExpressionEngine`); `Compare`/`TryToDouble`/`ParseLiteral`/`IsTruthy` kept in place.

## TDD Evidence

### Step 1-2: RED

Wrote `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ExpressionEngineTests.cs` (brief's 6
tests + 1 extra left-associativity test per task instructions). Ran:

```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionEngine
```

Result: FAIL — `error CS0246: 'ExpressionEngine' türü veya ad alanı adı bulunamadı` (type didn't
exist yet). Confirmed RED.

### Step 3-5: Implementation

Created:
- `src/RPA.Infrastructure/Workflow/Expressions/FunctionRegistry.cs` (public `ExpressionFunctionInfo`/
  `ExpressionFunctionParam`, internal `ExpressionFunction`, `FunctionRegistry.TryGet`/`Catalog`).
- `src/RPA.Infrastructure/Workflow/Expressions/DateFunctions.cs`, `StringFunctions.cs`,
  `ConversionFunctions.cs`, `HelperFunctions.cs` — empty `All => Array.Empty<ExpressionFunction>()`
  skeletons (Tasks 3-5 fill these in).
- `src/RPA.Infrastructure/Workflow/Expressions/ExpressionEngine.cs` — AST evaluator per brief.
- Rewired `src/RPA.Infrastructure/Workflow/ExpressionEvaluator.cs`: added `_engine` field, ctor
  wiring, delegated the 3 `ResolvePath` call-sites (`EvaluateValue`, `EvaluateString`,
  `ResolveOperand`) to `_engine.Evaluate(...)`, removed the old private `ResolvePath` method, and
  dropped the now-unused `Newtonsoft.Json.Linq` using directive.

### Bug found and fixed during GREEN pass (not in brief's verbatim code)

`ExpressionEngine.NormalizeNumber(double d)` as written in the brief:
```csharp
private static object NormalizeNumber(double d) =>
    d == Math.Floor(d) && !double.IsInfinity(d) ? (long)d : d;
```
This has a C# conditional-expression typing defect: the ternary's two branches are `long` and
`double`; C# resolves the *conditional expression's* type by finding an implicit conversion between
the branch types themselves (long→double exists, double→long doesn't), so the ternary evaluates as
`double` and gets boxed as `double` — **not** `long` — regardless of the method's `object` return
type. Verified via a debug probe: `Arithmetic("2","1","+")` with two `long` operands and
`integral=True` still produced `System.Double`, not `System.Int64`. Fixed by casting the `long`
branch to `object` explicitly:
```csharp
private static object NormalizeNumber(double d) =>
    d == Math.Floor(d) && !double.IsInfinity(d) ? (object)(long)d : d;
```
This forces the conditional's type to `object` (both branches implicitly convert to `object` via
boxing), preserving the intended `long` runtime type for integral results. All other engine code
(Compare/TryToDouble/ResolvePath/EvalFunction/EvalBinary) matches the brief verbatim.

### Step 6: GREEN

```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ExpressionEngine
```
Result: **7/7 passed** (6 brief tests + `Arithmetic_Subtraction_LeftAssociative`).

### Step 7: Backward-compatibility suite (critical acceptance bar)

```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~BaseRunner
```
Result: **29/29 passed** — all existing `${var}`/`{{var}}`/JSON-path/comparison/template scenarios
produce identical results through the new engine delegation.

### Full Infrastructure suite (sanity check, not required by brief)

```
dotnet test tests/RPA.Infrastructure.Tests
```
Result: 544/545 passed. The 1 failure (`SapGuiChannelTests.Channel_DoubleConnect_ThrowsBusinessException`)
is unrelated to expressions — it involves SAP GUI COM/STA-thread stub state and fails only under
full-suite parallel execution; it **passes in isolation**
(`dotnet test --filter FullyQualifiedName~Channel_DoubleConnect` → pass). Confirmed pre-existing/
flaky by inspection (STA thread + static session-manager state), not caused by this task's changes,
and entirely outside the expression-evaluator code path.

## Commit

`6ada950` — "feat(expr): AST evaluator + FunctionRegistry iskeleti + Evaluator delegasyonu"
(8 files changed: `ExpressionEvaluator.cs` modified; `FunctionRegistry.cs`, `ExpressionEngine.cs`,
`DateFunctions.cs`, `StringFunctions.cs`, `ConversionFunctions.cs`, `HelperFunctions.cs`,
`ExpressionEngineTests.cs` added).

## Files touched

- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\FunctionRegistry.cs` (new)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\ExpressionEngine.cs` (new)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\DateFunctions.cs` (new, skeleton)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\StringFunctions.cs` (new, skeleton)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\ConversionFunctions.cs` (new, skeleton)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\Expressions\HelperFunctions.cs` (new, skeleton)
- `C:\Source\RPA\src\RPA.Infrastructure\Workflow\ExpressionEvaluator.cs` (modified — delegation)
- `C:\Source\RPA\tests\RPA.Infrastructure.Tests\Workflow\Expressions\ExpressionEngineTests.cs` (new)
