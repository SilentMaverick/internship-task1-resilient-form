# Task 1 â€” resilient form checks (C# + Selenium)

Checks First Name, Gender and State on a configurable page. It first identifies the most plausible form region using its heading and the presence of the requested field labels, then searches only inside that region. This matters on the current CloudQA page: the rendered DOM has a main form, another visible â€œShadow DOMâ€ form, and more copies in iframes. Some control ids repeat, so an id-only lookup can target the wrong copy. The program uses accessible labels, placeholders and radio option values; it fails on ties instead of choosing an arbitrary copy.

## Requirements

- .NET 8 SDK
- Chrome or Chromium
- Network access on first restore (Selenium.WebDriver package)

## Run

```powershell
dotnet run --project ResilientForm.csproj -- --url https://app.cloudqa.io/home/AutomationPracticeForm --first-name Ada --gender Female --state India
```

Options: `--url`, `--first-name`, `--gender`, `--state`, `--headless`. Defaults are provided; pass `--help` for a summary.

The program fills the textbox, selects the requested radio option, selects the requested dropdown option, and verifies each resulting value. It exits nonzero on a failed assertion or an ambiguous/missing field. The URL and values can point at a modified deployment of the form.

**Observed page detail:** although the control is labeled â€œState,â€ the live practice page currently populates it with country names. The default `India` matches the live option list. Pass another `--state` value if your modified test page offers actual states. Navigation uses Seleniumâ€™s eager page-load strategy and waits only for a visible form region, instead of waiting for every embedded example and asset to finish loading.

## Approach and limitations

The locator uses semantic signals rather than a fixed field order or id/class selector. It adapts to reordered controls and common markup/attribute changes. It cannot infer intent if a redesign removes the useful heading and labels, presents indistinguishable duplicate forms, or replaces the native controls with inaccessible custom widgets. In those cases it stops with an ambiguity or missing-control diagnostic. This is heuristic resilience, not a guarantee against arbitrary redesigns.

