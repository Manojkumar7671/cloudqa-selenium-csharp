# CloudQaSelenium

A .NET 8 console application that uses Selenium WebDriver to fill a web form by semantic labels rather than fixed element IDs. It enters **First Name**, selects **Gender: Male**, selects **Country**, then selects the dependent **State**. Each operation prints a `PASS` or `FAIL`; any field failure returns a non-zero exit code.

## Requirements

- .NET 8 SDK
- Google Chrome or Chromium
- Internet access on first run if Selenium Manager must obtain a compatible ChromeDriver. An existing ChromeDriver on `PATH` is also supported by Selenium Manager.

## Run

From this repository root:

```bash
dotnet restore src/CloudQaSelenium
dotnet run --project src/CloudQaSelenium -- --url https://your-form.example/path
```

The sample `config.json` points to the local first fixture, not a production site. To use it, start the fixture server in another terminal:

```bash
python3 -m http.server 8765 --bind 127.0.0.1 --directory tests/fixtures
```

Then run `dotnet run --project src/CloudQaSelenium`. `config.json` provides URL, First Name, Country, State, timeout, headless mode, and optional Chrome binary. CLI arguments override it:

```bash
dotnet run --project src/CloudQaSelenium -- \
  --url http://127.0.0.1:8765/fixture-2.html \
  --first-name Avery --country 'United States' --state California \
  --timeout 15 --headless true
```

Run `dotnet run --project src/CloudQaSelenium -- --help` for the supported switches. CLI URL is validated as an absolute URI. `--timeout` accepts 1–300 seconds. The app uses explicit waits and does not configure an implicit wait.

## Locator behavior

`FindField(label, type)` ranks controls using, in order, explicit `label[for]` association and wrapping labels; nearby sibling and field/fieldset label text; accessible attributes (`aria-label`, placeholder, name, title); and a token-overlap fuzzy fallback against nearby container text. It filters candidates by control type and favors exact semantic matches so lookalike placeholder/decoy fields do not outrank the real field. Radio selection resolves the **Male** option label. A native `<select>` uses its actual options; custom accessible combobox/listbox controls are opened and their visible options are selected. The State control is located and enabled only after Country selection completes.

## Local fixtures and tests

Three checked-in fixtures deliberately vary IDs and markup: (1) associated labels and native selects with an exact-placeholder decoy; (2) reordered fields, wrapping labels, and custom dropdowns; (3) sibling text labels, unrelated IDs, lookalike decoys, and mixed custom/native dropdowns.

Run fixture validation without .NET:

```bash
python3 tests/smoke_fixtures.py
```

Run Selenium integration against all three local fixtures on a machine with the requirements installed:

```bash
bash tests/run-integration.sh
```

The integration runner builds once, starts a loopback-only fixture HTTP server, runs the app against each fixture, requires successful per-field output, and cleans up the server. It is also a command-line proof that the app selects actual values in the browser (the app verifies the resulting input/select/radio state after each interaction).

## Exit codes

- `0`: all four target fields passed.
- `1`: browser/navigation or one or more field interactions failed.
- `2`: invalid/missing URL or invalid configuration.
