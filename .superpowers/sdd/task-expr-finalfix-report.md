# Expression Function Library — Final Fixes (I1 + M1)

## Fix I1 — backward-compat for non-identifier variable names

**RED:**
```
Variable_NonIdentifierName_ResolvesViaWholeTokenFastPath [FAIL]
  BusinessException: Aritmetik '-' sayısal olmayan değere uygulandı.
```
`Evaluate("my-var")` was parsed as `my - var` (subtraction) instead of resolving the
whole-token variable name — confirming the regression described in the task.

**GREEN:** Added a fast-path in `ExpressionEngine.Evaluate(string)` (`src/RPA.Infrastructure/Workflow/Expressions/ExpressionEngine.cs`):
if the entire trimmed expression is exactly a known variable name in scope, resolve it
directly (bypassing `ExpressionParser`), normalizing `JToken` → native the same way
`ResolvePath` does. Pure identifiers and dotted paths still parse normally (fast-path misses
because the raw string with dots isn't a flat scope key).

**Unexpected regression discovered and fixed along the way:** while writing the dotted-path
regression test (`Variable_DottedPath_StillResolvesAfterFastPath`), found that
`VariableScope.JTokenToNative` (`src/RPA.Infrastructure/Workflow/VariableScope.cs`) — used by
`ExpressionEngine.ResolvePath` for nested JSON leaf values — returned a `Newtonsoft.Json.Linq.JValue`
instead of a plain `System.String` for string-typed tokens. Root cause: the C# switch-expression's
arm `(string?)token` was being re-boxed to `JToken` because the switch expression's inferred
common type picked up `JToken`'s implicit conversion operators from `string`/`long`/etc., overriding
the method's `object?` return-type target-typing. Rewrote the switch expression as sequential
`if`/`return` statements, which forces per-statement target typing to `object?` and returns the
plain `System.String`. This was a pre-existing latent bug (silently absorbed by `JValue`'s custom
`Equals` in some contexts) unmasked by the new fast-path test; fixed as part of I1 since the I1
regression test requires it to pass correctly.

**GREEN (final):**
```
Variable_NonIdentifierName_ResolvesViaWholeTokenFastPath [PASS]
Variable_DottedPath_StillResolvesAfterFastPath [PASS]
```

## Fix M1 — wrap raw System exceptions as BusinessException

**RED:**
```
Format_MalformedPattern_ThrowsBusiness [FAIL] — Actual: System.FormatException
ToDecimal_DoubleOverflow_ThrowsBusiness [FAIL] — Actual: System.OverflowException
```

**GREEN:**
- `DateFunctions.cs`: `Format` invoke body extracted to `FormatDate(a)`, wraps
  `AsDate(...).ToString(pattern, culture)` in try/catch `FormatException` → `ExpressionErrors.Business`.
- `ConversionFunctions.cs`: `ToDecimalImpl`'s `case double d:` wraps the `(decimal)d` cast in
  try/catch `OverflowException` → `ExpressionErrors.Business`. `ToDouble` routes through
  `ToDecimalImpl` so it is covered by the same fix.

```
Format_MalformedPattern_ThrowsBusiness [PASS]
ToDecimal_DoubleOverflow_ThrowsBusiness [PASS]
```

## Fixture grep — non-identifier variable name blast radius

Searched `tests/**/*.cs`, `**/*.workflow.json`, and other `${...}` usages in the repo
(`pilot/mm01-material-creation.workflow.json`, `tests/RPA.Infrastructure.Tests/PilotScenarioTests.cs`,
`src/RPA.Infrastructure/Components/SapLoginComponent.json`):

Found variable names: `materialName`, `materialNumber`, `recordId`, `client`, `credentialRef`,
`otpRequestId`, `systemName`, `text`. All are plain identifiers (letters/digits/underscore) —
**no hyphenated, space-containing, or keyword-shadowing variable names found in any real fixture.**
The I1 regression was not actively biting any existing fixture, but the fast-path fix restores the
documented contract and protects against future non-identifier variable names.

## Suite results

- `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~Expressions` → 55 passed, 0 failed
- `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~BaseRunner` → 29 passed, 0 failed
