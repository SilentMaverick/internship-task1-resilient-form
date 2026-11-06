using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

record Options(string Url, string FirstName, string Gender, string State, bool Headless);

static class Program
{
    static int Main(string[] argv)
    {
        try
        {
            var args = Parse(argv);
            var chrome = new ChromeOptions();
            chrome.PageLoadStrategy = PageLoadStrategy.Eager;
            if (args.Headless) chrome.AddArgument("--headless=new");
            chrome.AddArgument("--window-size=1440,1000");
            using var driver = new ChromeDriver(chrome);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(25);
            driver.Navigate().GoToUrl(args.Url);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.Until(d => d.FindElements(By.CssSelector("form,[role='form'],main,article,section"))
                .Any(e => e.Displayed && e.FindElements(By.CssSelector("input,select,textarea,[role='radio'],[role='combobox']")).Count > 0));

            // Scope every lookup to the best semantic form root. This avoids the
            // duplicated iframe/shadow examples present on the practice page.
            var root = FindFormRoot(driver);
            var first = FindControl(driver, root, "first name", e => IsInput(e, "text"));
            first.Clear();
            first.SendKeys(args.FirstName);
            Verify(Attr(first, "value") == args.FirstName, "First Name");

            var gender = FindControl(driver, root, args.Gender, e => IsInput(e, "radio"));
            gender.Click();
            Verify(gender.Selected, "Gender");

            var dropdown = FindControl(driver, root, "state", e => Tag(e, "select"));
            var select = new SelectElement(dropdown);
            var matchingOption = select.Options.FirstOrDefault(o => Norm(o.Text) == Norm(args.State));
            if (matchingOption is null)
                throw new InvalidOperationException($"Option '{args.State}' is not present in the State dropdown. Options: {string.Join(", ", select.Options.Select(o => o.Text))}");
            select.SelectByText(matchingOption.Text);
            Verify(Norm(select.SelectedOption.Text) == Norm(args.State), "State");

            Console.WriteLine($"PASS: First Name='{args.FirstName}', Gender='{args.Gender}', State='{args.State}' at {driver.Url}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    static IWebElement FindFormRoot(IWebDriver driver)
    {
        var roots = driver.FindElements(By.CssSelector("form,[role='form'],main,article,section"))
            .Where(e => e.Displayed && e.FindElements(By.CssSelector("input,select,textarea,[role='radio'],[role='combobox']")).Count > 0)
            .Select(e => (Element: e, Score: FormScore(e))).OrderByDescending(x => x.Score).ToList();
        if (roots.Count == 0) throw new InvalidOperationException("No visible form-like region with input controls was found.");
        if (roots.Count > 1 && roots[0].Score == roots[1].Score)
            throw new InvalidOperationException("More than one form region is equally plausible. Add a distinct heading or accessible form name.");
        return roots[0].Element;
    }

    static int FormScore(IWebElement root)
    {
        var text = Norm(root.Text);
        var score = Tag(root, "form") ? 6 : Attr(root, "role") == "form" ? 5 : 0;
        if (root.FindElements(By.XPath(".//*[self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6]")).Any(h => Norm(h.Text) == "form")) score += 20;
        foreach (var term in new[] { "first name", "gender", "state" }) if (text.Contains(term)) score += 4;
        return score;
    }

    static IWebElement FindControl(IWebDriver driver, IWebElement root, string requested, Func<IWebElement, bool> shape)
    {
        var target = Norm(requested);
        var candidates = root.FindElements(By.CssSelector("input,select,textarea,[role='radio'],[role='combobox']"))
            .Where(e => e.Displayed && e.Enabled && shape(e))
            .Select(e => (Element: e, Score: ControlScore(driver, e, target)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"Could not identify visible, enabled '{requested}' control inside the selected form.");
        if (candidates.Count > 1 && candidates[0].Score == candidates[1].Score)
            throw new InvalidOperationException($"Ambiguous '{requested}' control inside the selected form ({candidates.Count(x => x.Score == candidates[0].Score)} equally ranked candidates).");
        return candidates[0].Element;
    }

    static int ControlScore(IWebDriver driver, IWebElement element, string target)
    {
        var score = 0;
        ScoreText(Attr(element, "aria-label"), 12);
        ScoreText(Attr(element, "placeholder"), 9);
        ScoreText(Attr(element, "title"), 7);
        try
        {
            var semantics = (string)((IJavaScriptExecutor)driver).ExecuteScript(
                "const e=arguments[0]; const labels=e.labels?[...e.labels].map(x=>x.innerText).join(' '):''; const near=[e.nextSibling,e.previousSibling,e.nextElementSibling,e.previousElementSibling].map(n=>!n?'':n.nodeType===3?n.textContent:(n.matches&&n.matches('label,span,[role=radio]')?n.innerText:'' )).filter(x=>x&&x.trim()).join(' '); return labels+'\\u001f'+near", element)!;
            var parts = semantics.Split('\u001f', 2);
            ScoreText(parts[0], 15);
            if (parts.Length > 1) ScoreText(parts[1], 17);
        }
        catch (WebDriverException) { /* Detached/stale candidates will fail later with a useful locator error. */ }

        var value = Norm(Attr(element, "value"));
        if (value == target && Tag(element, "input") && Attr(element, "type").Equals("radio", StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 10);
        return score;

        void ScoreText(string text, int weight)
        {
            var normalized = Norm(text);
            if (normalized.Length == 0) return;
            if (normalized == target) score = Math.Max(score, weight);
            else if (normalized.Contains(target) || (target.Contains(normalized) && normalized.Length >= 4)) score = Math.Max(score, weight - 2);
        }
    }

    static bool IsInput(IWebElement e, string type) => Tag(e, "input") && Attr(e, "type").Equals(type, StringComparison.OrdinalIgnoreCase);
    static bool Tag(IWebElement e, string tag) => e.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase);
    static string Attr(IWebElement e, string name) => e.GetAttribute(name) ?? "";
    static string Norm(string value) => Regex.Replace(value ?? "", "[^a-z0-9]+", " ", RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
    static void Verify(bool condition, string field) { if (!condition) throw new InvalidOperationException($"{field} did not retain the requested value."); }

    static Options Parse(string[] argv)
    {
        if (argv.Contains("--help"))
        {
            Console.WriteLine("--url URL --first-name NAME --gender OPTION --state OPTION --headless");
            Environment.Exit(0);
        }
        var values = new Dictionary<string, string>();
        var flags = new HashSet<string> { "--headless" };
        var valueKeys = new HashSet<string> { "--url", "--first-name", "--gender", "--state" };
        for (var i = 0; i < argv.Length; i++)
        {
            var key = argv[i];
            if (flags.Contains(key)) continue;
            if (!valueKeys.Contains(key)) throw new ArgumentException($"Unknown option '{key}'. Use --help for usage.");
            if (i + 1 >= argv.Length || argv[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option '{key}' requires a value.");
            values[key] = argv[++i];
        }
        return new Options(
            values.GetValueOrDefault("--url", "https://app.cloudqa.io/home/AutomationPracticeForm"),
            values.GetValueOrDefault("--first-name", "Ada"),
            values.GetValueOrDefault("--gender", "Female"),
            values.GetValueOrDefault("--state", "India"),
            argv.Contains("--headless"));
    }
}

