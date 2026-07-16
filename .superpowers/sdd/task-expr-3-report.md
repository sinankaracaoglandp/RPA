# Task 3 Report: Tarih fonksiyonları

## TDD

RED: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~DateFunctions`
→ 6 failed / 1 passed (`Bilinmeyen fonksiyon: 'Format'/'ToDate'/'Year'/'DateDiffDays'` — DateFunctions.All was empty skeleton).

Implemented `FunctionArgs.cs` (AsDate/AsString/AsInt/Culture/P) and `DateFunctions.All`
(Now, Today, AddDays, AddMonths, AddYears, AddHours, AddMinutes, Format, ToDate, Year, Month, Day,
DayOfWeek, DateDiffDays) exactly per brief.

GREEN: same filter → 7/7 passed (44 ms).

## Files
- Created: `src/RPA.Infrastructure/Workflow/Expressions/FunctionArgs.cs`
- Modified: `src/RPA.Infrastructure/Workflow/Expressions/DateFunctions.cs`
- Created: `tests/RPA.Infrastructure.Tests/Workflow/Expressions/DateFunctionsTests.cs`

## Commit
f224304 feat(expr): tarih fonksiyonlari (Now/AddDays/Format/ToDate/...)
