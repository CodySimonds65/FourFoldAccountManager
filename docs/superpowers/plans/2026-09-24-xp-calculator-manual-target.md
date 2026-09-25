# XP Calculator Manual Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the XP calc tab's target-level field blank until the user enters a level, while preserving a manual target on refresh of the same account.

**Architecture:** The Core calculator state represents a loaded profile with no selected target. The WPF panel owns the text field and remembers whether a snapshot belongs to the same account. Calculated output exists only after a valid manual target is applied.

**Tech Stack:** .NET 10, C#, WPF, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-24-xp-calculator-and-leaderboard-accuracy-design.md`.

## Global Constraints

- Change only the XP calc tab's target-level behavior. The XP tracker's time-to-next-level estimate remains automatic.
- Do not add persisted target preferences or change the XP curve formula.
- Keep existing automatic recalculation when a valid target is typed or Apply is clicked.

## Review Focus

- A newly loaded profile has a blank target and no next-level projection; test in Task 1 and inspect in Task 2.
- Clearing a previously valid target removes its old projection immediately; test in Task 1 and inspect in Task 2.
- A same-account refresh preserves the user's target and uses the new profile XP; inspect in Task 2.
- Changing accounts clears the target, including when both accounts have the same active-class level; inspect in Task 2.
- An unavailable active class does not display an old projection; test in Task 1 and inspect in Task 2.

## File Map

| File | Responsibility |
| --- | --- |
| `src/FourFoldAccountManager.Core/Calculation/ExperienceCalculatorState.cs` | Represent a profile that has no target and clear an existing target. |
| `src/FourFoldAccountManager.Core.Tests/Calculation/ExperienceCalculatorStateTests.cs` | Verify no-target, target, clear, and unavailable states. |
| `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs` | Keep the input blank on account change and preserve manual input on same-account refresh. |
| `README.md` | Describe manual target entry. |

---

### Task 1: Represent an unselected target in Core

**Files:**
- Create: `src/FourFoldAccountManager.Core.Tests/Calculation/ExperienceCalculatorStateTests.cs`
- Modify: `src/FourFoldAccountManager.Core/Calculation/ExperienceCalculatorState.cs`

**Interfaces:**
- Consumes: `ClassProfileSnapshot`, `PlayerProgressSnapshot`, and `ExperienceCurve.Project`.
- Produces: `ExperienceCalculatorState.FromProfile`, `FromSnapshot`, `WithTarget(long)`, and new `WithoutTarget()`; `TargetLevel == 0` means no target is selected.

- [ ] **Step 1: Write focused failing tests.** Use a level-2 Warrior profile with 5/30 XP. Assert that `FromProfile` and `FromSnapshot` have `TargetLevel == 0`, `Projection == null`, `RemainingXpText == "—"`, and retain the current class/level; `WithTarget(3)` has 25 remaining XP; `WithoutTarget()` clears the projection; and an unavailable active class remains unavailable.

```csharp
var profile = new ClassProfileSnapshot(2, 5, 30, null) { ClassName = "Warrior" };
var initial = ExperienceCalculatorState.FromProfile(profile);
Assert.Equal(0, initial.TargetLevel);
Assert.Null(initial.Projection);
Assert.Equal("—", initial.RemainingXpText);
Assert.Equal("Warrior", initial.ClassName);
Assert.Equal(2, initial.CurrentLevel);
var selected = initial.WithTarget(3);
Assert.Equal("25", selected.RemainingXpText);
Assert.Null(selected.WithoutTarget().Projection);
var snapshot = new PlayerProgressSnapshot("Alice", "Warrior",
    new Dictionary<string, ClassProfileSnapshot> { ["Warrior"] = profile }, []);
Assert.Equal(0, ExperienceCalculatorState.FromSnapshot(snapshot).TargetLevel);
Assert.Null(ExperienceCalculatorState.FromSnapshot(snapshot with
    { ActiveClassName = null }).Projection);
```

- [ ] **Step 2: Run the new test to confirm the current next-level default fails.**

```powershell
dotnet test src/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ExperienceCalculatorStateTests
```

- [ ] **Step 3: Make `FromProfile` return a profile-backed unselected state and add `WithoutTarget()`.** Keep `WithTarget(long)` as the only path to `Create(profile, targetLevel)`. Use `TargetLevel = 0`, `Projection = null`, `IsValid = false`, and placeholder output for the unselected state. Keep unavailable-profile messages unchanged.

```csharp
public static ExperienceCalculatorState FromProfile(ClassProfileSnapshot profile) =>
    Unselected(profile ?? throw new ArgumentNullException(nameof(profile)));

public ExperienceCalculatorState WithoutTarget() =>
    Profile is null ? this : Unselected(Profile);

private static ExperienceCalculatorState Unselected(ClassProfileSnapshot profile) =>
    new(false, "Enter a target level.", profile.ClassName, profile.Level, 0,
        "—", "—", "—", 0, false, null, profile);
```

- [ ] **Step 4: Run the focused and Core test projects.**

```powershell
dotnet test src/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
```

- [ ] **Step 5: Commit the Core change.**

```powershell
git add src/FourFoldAccountManager.Core/Calculation/ExperienceCalculatorState.cs src/FourFoldAccountManager.Core.Tests/Calculation/ExperienceCalculatorStateTests.cs
git commit -m "fix: require manual xp calculator target"
```

### Task 2: Make the XP calc field follow account identity

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: the Core state methods from Task 1 and `AccountProfile.Id`.
- Produces: blank target text for a new account, preserved target text on a same-account refresh, and immediate projection clearing when the field is emptied.

- [ ] **Step 1: Add a snapshot-account ID to the panel and capture the current target text only when `SetSnapshot` receives the same ID.** Set `_updatingTarget` while changing `TargetLevelBox.Text`; create an unselected state from the new snapshot, then apply the preserved text only if it parses as a positive integer. Treat an invalid or partially typed preserved string as unselected without changing the user's text.

```csharp
private Guid? _snapshotAccountId;

// At the start of SetSnapshot:
var targetText = _snapshotAccountId == account.Id ? TargetLevelBox.Text : string.Empty;
_snapshotAccountId = account.Id;
SetSelectedAccount(account);
_state = ExperienceCalculatorState.FromSnapshot(snapshot);
_updatingTarget = true;
TargetLevelBox.Text = targetText;
_updatingTarget = false;
if (long.TryParse(targetText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
        out var targetLevel) && targetLevel > 0)
    _state = _state.WithTarget(targetLevel);
Render();
```

- [ ] **Step 2: On blank or invalid text, call `_state.WithoutTarget()` and render placeholders; then show the existing input guidance.** Clear `_snapshotAccountId` in `ClearSnapshot`. Keep Apply and typing behavior aligned so neither can leave a stale projection.

```csharp
if (_state?.Profile is null) return;
if (!long.TryParse(TargetLevelBox.Text.Trim(), NumberStyles.None,
        CultureInfo.InvariantCulture, out var targetLevel) || targetLevel <= 0)
{
    _state = _state.WithoutTarget();
    Render();
    StatusText.Text = "Enter a positive target level.";
    return;
}
_state = _state.WithTarget(targetLevel);
Render();
```

- [ ] **Step 3: Update README's XP Calculator description to say that the user enters a target level.**

```markdown
- **XP Calculator** shows the active class and calculates XP to a target level after you enter one.
```

- [ ] **Step 4: Build and perform the WPF walkthrough.** Open XP calc for account A: target blank and projection empty. Enter a valid target: projection appears. Refresh A: text persists and numbers update. Clear target: projection disappears. Switch to account B: target blank. Confirm the XP tracker still shows its automatic time-to-next-level estimate.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
```

- [ ] **Step 5: Commit the panel and documentation.**

```powershell
git add src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs README.md
git commit -m "fix: leave xp calculator target blank"
```
