using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

namespace CloudQaSelenium;

internal sealed class FormAutomation : IDisposable
{
    private readonly AppSettings _settings;
    private IWebDriver? _driver;
    private WebDriverWait? _wait;
    private static readonly By Fields = By.XPath("//input[not(translate(@type,'HIDDEN','hidden')='hidden')] | //select | //textarea | //*[@role='combobox' or @aria-haspopup='listbox' or @data-dropdown]");

    public FormAutomation(AppSettings settings) => _settings = settings;

    public int Execute()
    {
        try
        {
            _driver = CreateDriver();
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            _driver.Navigate().GoToUrl(_settings.Url);
            _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Browser/navigation error: {ex.Message}");
            foreach (var name in new[] { "First Name", "Gender (Male)", "Country", "State" })
                Print(name, false, "browser could not be initialized or page could not be loaded");
            return 1;
        }

        var failures = 0;
        if (!RunField("First Name", () => FillText("First Name", _settings.FirstName))) failures++;
        if (!RunField("Gender (Male)", () => SelectRadio("Male"))) failures++;
        var countryOk = RunField("Country", () => SelectOption("Country", _settings.Country));
        if (!countryOk) failures++;
        if (!countryOk)
        {
            Print("State", false, "not attempted because Country selection failed");
            failures++;
        }
        else if (!RunField("State", () => SelectOption("State", _settings.State))) failures++;
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Finds a control using label association, nearby label/ancestor/sibling text, accessible attributes, then fuzzy text.</summary>
    private IWebElement FindField(string label, string type)
    {
        var wait = _wait ?? throw new InvalidOperationException("Browser is not initialized.");
        return wait.Until(driver =>
        {
            try { return FindBestCandidate(driver, label, type); }
            catch (StaleElementReferenceException) { return null; }
        }) ?? throw new WebDriverTimeoutException($"Timed out finding {type} field '{label}'.");
    }

    private static IWebElement? FindBestCandidate(IWebDriver driver, string label, string type)
    {
        var query = Normalize(label);
        var candidates = driver.FindElements(Fields).Where(e => MatchesType(e, type)).ToList();
        IWebElement? best = null;
        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var associatedLabels = new List<string>();
                var contextualLabels = new List<string>();
                var id = candidate.GetAttribute("id") ?? "";
                if (!string.IsNullOrWhiteSpace(id))
                {
                    foreach (var node in driver.FindElements(By.XPath($"//label[@for={XPathLiteral(id)}]")))
                        AddText(associatedLabels, node.Text);
                }
                foreach (var node in candidate.FindElements(By.XPath("ancestor::label[1]"))) AddText(associatedLabels, node.Text);
                foreach (var node in candidate.FindElements(By.XPath("preceding-sibling::*[1] | following-sibling::*[1]"))) AddText(contextualLabels, node.Text);
                foreach (var node in candidate.FindElements(By.XPath("ancestor::fieldset[1]//legend | ancestor::*[position() <= 2]/*[self::label or self::legend or self::span] | ancestor::*[contains(@class,'field') or contains(@class,'form-group')][position() <= 2]//label | ancestor::*[contains(@class,'field') or contains(@class,'form-group')][position() <= 2]//span"))) AddText(contextualLabels, node.Text);

                var attrs = new[] { "aria-label", "placeholder", "name", "title", "data-label" }
                    .Select(a => candidate.GetAttribute(a) ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                var groupAndOption = string.Join(" ", contextualLabels.Concat(associatedLabels));
                var score = 0;
                foreach (var text in associatedLabels) score = Math.Max(score, LabelScore(query, text, 130));
                foreach (var text in contextualLabels) score = Math.Max(score, LabelScore(query, text, 90));
                if (type.Equals("radio", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(groupAndOption))
                    score = Math.Max(score, LabelScore(query, groupAndOption, 125));
                foreach (var attr in attrs) score = Math.Max(score, LabelScore(query, attr, 82));

                // Last-resort fuzzy fallback from nearby container text; exact and accessible labels always rank higher.
                if (score == 0)
                {
                    var nearby = candidate.FindElements(By.XPath("ancestor::*[self::div or self::section or self::fieldset][position() <= 3]"));
                    foreach (var node in nearby)
                    {
                        var text = node.Text;
                        if (text.Length <= 350) score = Math.Max(score, LabelScore(query, text, 54));
                    }
                }
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            catch (StaleElementReferenceException) { }
        }
        return best;
    }

    private void FillText(string label, string value)
    {
        var field = FindField(label, "text");
        WaitUntilEnabled(field, label);
        field.Clear();
        field.SendKeys(value);
        _wait!.Until(_ => string.Equals(field.GetAttribute("value"), value, StringComparison.Ordinal));
    }

    private void SelectRadio(string optionLabel)
    {
        var radio = FindField(optionLabel, "radio");
        WaitUntilEnabled(radio, optionLabel);
        if (!radio.Selected) radio.Click();
        _wait!.Until(_ => radio.Selected);
    }

    private void SelectOption(string label, string desired)
    {
        // For dependent controls, wait until the control becomes enabled after its parent selection.
        var field = FindField(label, "select");
        WaitUntilEnabled(field, label);
        if (string.Equals(field.TagName, "select", StringComparison.OrdinalIgnoreCase))
        {
            var select = new SelectElement(field);
            var option = select.Options
                .Select(o => new { Element = o, Score = LabelScore(Normalize(desired), o.Text, 100) })
                .OrderByDescending(x => x.Score).FirstOrDefault(x => x.Score > 0)
                ?? throw new NoSuchElementException($"No option matching '{desired}' in {label}.");
            select.SelectByText(option.Element.Text);
            _wait!.Until(_ => select.SelectedOption.Text.Trim().Equals(option.Element.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            return;
        }

        field.Click();
        var optionElement = _wait!.Until(driver => FindDropdownOption(driver, desired));
        if (optionElement is null) throw new WebDriverTimeoutException($"No visible custom dropdown option matching '{desired}' for {label}.");
        optionElement.Click();
        var expected = Normalize(desired);
        _wait.Until(_ =>
        {
            try
            {
                var visible = Normalize(string.Join(" ", field.Text, field.GetAttribute("aria-valuetext"), field.GetAttribute("value")));
                return visible.Contains(expected, StringComparison.Ordinal) || Normalize(field.GetAttribute("aria-label") ?? "").Contains(expected, StringComparison.Ordinal);
            }
            catch (StaleElementReferenceException) { return false; }
        });
    }

    private static IWebElement? FindDropdownOption(IWebDriver driver, string desired)
    {
        var options = driver.FindElements(By.XPath("//*[@role='option' or @data-option or (self::li and ancestor::*[@role='listbox'])]"))
            .Where(o => { try { return o.Displayed && o.Enabled; } catch (StaleElementReferenceException) { return false; } })
            .Select(o => new { Element = o, Score = LabelScore(Normalize(desired), o.Text, 100) })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToList();
        return options.FirstOrDefault()?.Element;
    }

    private static bool MatchesType(IWebElement element, string type)
    {
        var tag = element.TagName.ToLowerInvariant();
        var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        return type.ToLowerInvariant() switch
        {
            "radio" => tag == "input" && inputType == "radio",
            "text" => tag == "textarea" || (tag == "input" && inputType is "text" or "search"),
            "select" => tag == "select" || (element.GetAttribute("role") ?? "").Equals("combobox", StringComparison.OrdinalIgnoreCase) || (element.GetAttribute("aria-haspopup") ?? "").Equals("listbox", StringComparison.OrdinalIgnoreCase) || element.GetAttribute("data-dropdown") is not null,
            _ => false
        };
    }

    private void WaitUntilEnabled(IWebElement element, string label)
    {
        _wait!.Until(_ =>
        {
            try { return element.Enabled && element.GetAttribute("aria-disabled") != "true"; }
            catch (StaleElementReferenceException) { return false; }
        });
    }

    private bool RunField(string name, Action action)
    {
        try { action(); Print(name, true, "interaction and value/selection verified"); return true; }
        catch (Exception ex) { Print(name, false, ex.Message); return false; }
    }

    private static int LabelScore(string normalizedQuery, string text, int exactWeight)
    {
        var normalizedText = Normalize(text);
        if (normalizedText.Length == 0 || normalizedQuery.Length == 0) return 0;
        if (normalizedText == normalizedQuery) return exactWeight;
        if (normalizedText.StartsWith(normalizedQuery + " ", StringComparison.Ordinal)) return exactWeight * 3 / 4;
        if (normalizedText.Contains(normalizedQuery, StringComparison.Ordinal)) return exactWeight / 2;
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var textTokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (queryTokens.Count == 0) return 0;
        var overlap = queryTokens.Count(textTokens.Contains);
        if (overlap == 0) return 0;
        var ratio = (double)overlap / queryTokens.Count;
        return ratio >= .5 ? (int)(exactWeight * .42 * ratio) : 0;
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
    private static void AddText(List<string> target, string? text)
    {
        var cleaned = text?.Trim();
        if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length <= 250 && !target.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) target.Add(cleaned);
    }
    private static string XPathLiteral(string value)
    {
        if (!value.Contains('\'')) return $"'{value}'";
        if (!value.Contains('"')) return $"\"{value}\"";
        return "concat(" + string.Join(", \"'\", ", value.Split('\'' ).Select(s => $"'{s}'")) + ")";
    }
    private static void Print(string field, bool passed, string detail) => Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {field}: {detail}");

    private IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        if (_settings.Headless) options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1440,1000");
        var binary = _settings.ChromeBinary;
        if (string.IsNullOrWhiteSpace(binary))
        {
            foreach (var candidate in new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable" })
                if (File.Exists(candidate)) { binary = candidate; break; }
        }
        if (!string.IsNullOrWhiteSpace(binary)) options.BinaryLocation = binary;
        return new ChromeDriver(options);
    }

    public void Dispose() => _driver?.Quit();
}
