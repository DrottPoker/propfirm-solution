namespace Prop.Api.Configuration;

/// <summary>Where the trading platform's admin API is, and how the event stream is read.</summary>
public sealed class TradingPlatformOptions
{
    public const string SectionName = "TradingPlatform";

    /// <summary>The trading service, for example http://localhost:5101/.</summary>
    public Uri? Url { get; init; }

    /// <summary>How long a request for events waits for new ones. The trading platform allows up to 30 seconds.</summary>
    public int EventWaitSeconds { get; init; } = 30;

    public int EventsPerRequest { get; init; } = 500;

    /// <summary>The prop platform's key to the partner API, which creates the servers of firms that sign up. A secret.</summary>
    public string PartnerApiKey { get; init; } = "";
}

/// <summary>Our own platform, where firms sign up, and where the portals of new firms are reached.</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>The product's name, shown when firms sign up, in our admin view and in emails (ADR 0046).</summary>
    public string Name { get; init; } = "Kronant Prop";

    /// <summary>Where firms sign up, ending with /, for example https://app.example.com/.</summary>
    public Uri? Url { get; init; }

    /// <summary>The portal address of a new firm, with {firm} for its short name, for example https://{firm}.example.com/.</summary>
    public string FirmPortalUrl { get; init; } = "";

    /// <summary>
    /// Where the internet reaches this service's firm API and payment webhooks, ending with /, for example
    /// https://api.example.com/. Shown to firms as the address for their payment provider's webhooks.
    /// </summary>
    public Uri? ApiUrl { get; init; }

    /// <summary>
    /// Our own admin view, where our staff review firms, ending with /, for example https://ops.example.com/. On its
    /// own host, so its session never reaches the sign-up or a firm's portal (ADR 0021).
    /// </summary>
    public Uri? OpsUrl { get; init; }

    /// <summary>The portal address of the firm with the short name.</summary>
    public Uri PortalUrlOf(string firmId) => new(FirmPortalUrl.Replace("{firm}", firmId, StringComparison.Ordinal));
}

/// <summary>Our own staff, who review firms in our admin view (ADR 0021).</summary>
public sealed class StaffOptions
{
    public const string SectionName = "Staff";

    /// <summary>Staff created at startup, or given the configured password. For development, until staff are invited.</summary>
    public IReadOnlyList<SeedLoginOptions> SeedUsers { get; init; } = [];
}

/// <summary>How firms sign up.</summary>
public sealed class SignupOptions
{
    public const string SectionName = "Signup";

    /// <summary>Whether the email address must be confirmed before the firm is created. Development turns it off.</summary>
    public bool RequireEmailVerification { get; init; } = true;

    /// <summary>The most confirmation emails one address gets in a day, so sign-ups cannot be used to flood it (ADR 0045).</summary>
    public int MaxEmailsPerDay { get; init; } = 3;

    /// <summary>The version of the terms and the data processing agreement that firms accept, recorded with each firm.</summary>
    public string TermsVersion { get; init; } = "";

    public Uri? TermsUrl { get; init; }

    public Uri? DpaUrl { get; init; }

    /// <summary>Short names firms may not choose, besides the built-in ones.</summary>
    public IReadOnlyList<string> ReservedFirmIds { get; init; } = [];

    /// <summary>The account currencies a firm may choose when it signs up, the first by default. The trading platform must offer them.</summary>
    public IReadOnlyList<string> Currencies { get; init; } = [];

    /// <summary>The currency a firm gets when it chooses none.</summary>
    public string DefaultCurrency => Currencies.Count > 0 ? Currencies[0] : "USD";
}

/// <summary>What a firm in the sandbox may do before it goes live.</summary>
public sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    /// <summary>The most challenge accounts a firm in the sandbox may have open at once.</summary>
    public int MaxOpenAccounts { get; init; } = 10;

    /// <summary>The most invitations to its team a firm may send in 30 days before we approve it (ADR 0043).</summary>
    public int MaxAdminInvites { get; init; } = 10;

    /// <summary>A sandbox whose administrators have not used the admin panel for this many days closes until they come back (ADR 0045).</summary>
    public int IdleDays { get; init; } = 60;
}

/// <summary>How challenges are bought in the firms' portals (ADR 0019).</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// How long a buyer has to pay before the order expires. Stripe needs 30 minutes to 24 hours. A payment that
    /// arrives later still counts.
    /// </summary>
    public TimeSpan OrderLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Stripe's API, ending with /.</summary>
    public Uri StripeApiUrl { get; init; } = new("https://api.stripe.com/");

    /// <summary>Whether firms that are live may also take test payments. For development only: the sandbox always may.</summary>
    public bool TestPaymentsForLiveFirms { get; init; }
}

/// <summary>
/// What firms pay us for their slots, and how (ADR 0020). A slot is room for one open challenge. The firm pays
/// in advance by card: a startup fee when it goes live, and each month a package with some slots and any slots
/// beyond it. The prices are our proposal until they are confirmed.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Stripe with our own account, or Test for a page in the portal that pays without money. Test is for development only.</summary>
    public string Provider { get; init; } = "Stripe";

    public string Currency { get; init; } = "USD";

    /// <summary>Paid once, when the firm goes live. 0 for none.</summary>
    public decimal StartupFee { get; init; }

    /// <summary>
    /// Paid when the firm sends its application for our review, and taken off the startup fee when it goes live.
    /// Never paid back. 0 for none. At most the startup fee (ADR 0021).
    /// </summary>
    public decimal ReviewDeposit { get; init; }

    /// <summary>The monthly price of the package, which includes <see cref="PackageSlots"/>.</summary>
    public decimal PackagePrice { get; init; }

    /// <summary>The slots the package includes, and so the fewest a firm can have.</summary>
    public int PackageSlots { get; init; }

    /// <summary>
    /// The monthly price of each slot beyond the package, from the slot each tier starts at. The first tier starts
    /// right after the package's slots. Every slot costs the price of its own tier, so the price per slot falls
    /// with more slots and never jumps.
    /// </summary>
    public IReadOnlyList<SlotPriceOptions> SlotPrices { get; init; } = [];

    public int MaxSlots { get; init; } = 10_000;

    /// <summary>How many days before a month starts it is charged, so a failed card can be replaced in time.</summary>
    public int ChargeDaysBeforeMonth { get; init; } = 5;

    /// <summary>How long after a declined monthly charge the card is tried again.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>How many times a monthly charge is tried before only the firm can pay it.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>When this share of the slots is used, the firm's administrators are warned.</summary>
    public int WarningPercent { get; init; } = 80;

    /// <summary>How long a checkout page for a payment or a card stays open. Stripe needs 30 minutes to 24 hours.</summary>
    public TimeSpan CheckoutLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Our own Stripe account's secret key. A secret: keep it out of files outside development.</summary>
    public string StripeSecretKey { get; init; } = "";

    /// <summary>Signs Stripe's webhooks about our own payments. A secret.</summary>
    public string StripeWebhookSecret { get; init; } = "";

    /// <summary>Our VAT in percent, added to the prices for firms that pay it (ADR 0032).</summary>
    public decimal VatPercent { get; init; } = 25;

    /// <summary>Who we are on the firms' invoices.</summary>
    public SellerOptions Seller { get; init; } = new();

    /// <summary>What firms pay for our built-in ID checks (ADR 0042).</summary>
    public IdentityCheckPriceOptions IdentityChecks { get; init; } = new();
}

/// <summary>
/// The built-in ID checks as an addition to the monthly package: a monthly price with some checks included, and a
/// price for each check beyond them and for the extra checks a firm turns on. Without VAT, like every price.
/// </summary>
public sealed class IdentityCheckPriceOptions
{
    /// <summary>Charged for each month the built-in checks are on when the month is charged.</summary>
    public decimal MonthlyPrice { get; init; } = 15m;

    /// <summary>The checks each monthly charge includes.</summary>
    public int Included { get; init; } = 25;

    /// <summary>Each check beyond those included.</summary>
    public decimal PerCheck { get; init; } = 0.8m;

    /// <summary>Each check that also checked the trader's address.</summary>
    public decimal Address { get; init; } = 0.3m;

    /// <summary>Each check that also screened the trader against sanctions and PEP lists.</summary>
    public decimal Sanctions { get; init; } = 0.3m;
}

/// <summary>
/// Who checks traders' IDs for the firms that use our built-in checks (ADR 0042): Didit, or Test for a page in the
/// portal that approves or declines without a real check, for development. Firms in the sandbox always get Test.
/// </summary>
public sealed class IdentityCheckOptions
{
    public const string SectionName = "Identity";

    public string Provider { get; init; } = "Didit";

    public DiditOptions Didit { get; init; } = new();
}

/// <summary>
/// Our Didit application: its API, the key, the secret that signs its webhooks, and the workflow for each set of
/// checks a firm can choose. Each workflow has the ID document, liveness and face match; the others add proof of address,
/// sanctions screening or both. The key and the secret are secrets.
/// </summary>
public sealed class DiditOptions
{
    public Uri ApiUrl { get; init; } = new("https://verification.didit.me/");

    public string ApiKey { get; init; } = "";

    public string WebhookSecret { get; init; } = "";

    public string Workflow { get; init; } = "";

    public string WorkflowWithAddress { get; init; } = "";

    public string WorkflowWithSanctions { get; init; } = "";

    public string WorkflowWithAddressAndSanctions { get; init; } = "";
}

/// <summary>Our company as invoices name it. Required outside development.</summary>
public sealed class SellerOptions
{
    public string Name { get; init; } = "";

    /// <summary>The address, with a line break between lines.</summary>
    public string Address { get; init; } = "";

    /// <summary>Where we are registered, as a two-letter code. A firm in the same country pays our VAT.</summary>
    public string Country { get; init; } = "SE";

    public string OrganizationNumber { get; init; } = "";

    public string VatNumber { get; init; } = "";

    /// <summary>Where firms write about their invoices.</summary>
    public string Email { get; init; } = "";

    public bool IsComplete => Name.Length > 0 && Address.Length > 0 && OrganizationNumber.Length > 0 && VatNumber.Length > 0;
}

/// <summary>The monthly price of each slot from slot number <see cref="From"/> on.</summary>
public sealed class SlotPriceOptions
{
    public int From { get; init; }

    public decimal Price { get; init; }
}

/// <summary>Email from the platform, such as confirmations and invitations for administrators.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; init; } = "";

    public string FromName { get; init; } = "";

    public SmtpOptions Smtp { get; init; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    public string UserName { get; init; } = "";

    /// <summary>A secret: keep it out of files outside development.</summary>
    public string Password { get; init; } = "";

    /// <summary>None, StartTls or SslOnConnect. StartTls everywhere but a local test server.</summary>
    public string Security { get; init; } = "StartTls";
}

/// <summary>The key that encrypts the firms' secrets in the database. Never kept in the database itself.</summary>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    /// <summary>32 random bytes in base64. A secret: keep it out of files outside development, and never change it without encrypting the secrets again.</summary>
    public string Key { get; init; } = "";
}

/// <summary>How often one client may call, so nobody can overload the service or fill its database (ADR 0045). 0 turns a limit off.</summary>
public sealed class LimitsOptions
{
    public const string SectionName = "Limits";

    /// <summary>Support tickets and messages one address may write in a minute, as a trader or an administrator.</summary>
    public int SupportWritesPerMinute { get; init; } = 20;

    /// <summary>Calls one key may make to the firm API in a minute.</summary>
    public int FirmApiCallsPerMinute { get; init; } = 600;
}

/// <summary>Rules for portal passwords, logins and sessions. Development turns them off; elsewhere the defaults hold.</summary>
public sealed class LoginOptions
{
    public const string SectionName = "Login";

    /// <summary>The shortest password a trader may choose.</summary>
    public int MinimumPasswordLength { get; init; } = 10;

    /// <summary>Login attempts per minute from one address, or 0 for no limit.</summary>
    public int AttemptsPerMinute { get; init; } = 10;

    /// <summary>How long a session lasts without being used.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(12);
}

/// <summary>
/// A configured firm, saved to the database at every start and live from the beginning. For development and
/// tests. Other firms sign up themselves (ADR 0017).
/// </summary>
public sealed class FirmOptions
{
    public const string SectionName = "Firms";

    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>SHA-256 of the key the firm's own systems use for the firm API, as lowercase hex.</summary>
    public string ApiKeySha256 { get; init; } = "";

    public FirmTradingOptions Trading { get; init; } = new();

    public FirmWebhookOptions Webhook { get; init; } = new();

    public FirmPortalOptions Portal { get; init; } = new();

    public FirmPaymentOptions Payments { get; init; } = new();

    /// <summary>How many challenges the firm can have open at once, without paying for them. Empty for no limit.</summary>
    public int? Slots { get; init; }

    /// <summary>Challenges created at startup, or replaced so they follow their template.</summary>
    public IReadOnlyList<SeedChallengeOptions> SeedChallenges { get; init; } = [];

    /// <summary>Administrators created at startup, or given the configured password. For development, until firms sign up themselves.</summary>
    public IReadOnlyList<SeedLoginOptions> SeedAdmins { get; init; } = [];

    /// <summary>Traders created at startup, or given the configured password. For development, so testing needs no invitation.</summary>
    public IReadOnlyList<SeedLoginOptions> SeedTraders { get; init; } = [];
}

/// <summary>The firm's white label portal: where it is reached and how it looks.</summary>
public sealed class FirmPortalOptions
{
    /// <summary>The portal's main address, ending with /, used in invitation links. For example https://portal.firm.com/.</summary>
    public Uri? Url { get; init; }

    /// <summary>Host names the portal is reached on, without port. The firm is known from them.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>An absolute https address, or empty for none.</summary>
    public string LogoUrl { get; init; } = "";

    /// <summary>Overrides of the portal's theme colors, as #rrggbb.</summary>
    public IReadOnlyDictionary<string, string> Colors { get; init; } = new Dictionary<string, string>();
}

/// <summary>How the configured firm's portal takes payment. The Stripe keys are secrets: keep them out of files outside development.</summary>
public sealed class FirmPaymentOptions
{
    /// <summary>Test, Stripe or External. Empty for no shop in the portal.</summary>
    public string Provider { get; init; } = "";

    public string StripeSecretKey { get; init; } = "";

    public string StripeWebhookSecret { get; init; } = "";

    /// <summary>The firm's own checkout page, for External.</summary>
    public Uri? CheckoutUrl { get; init; }

    /// <summary>The firm's terms, which buyers accept before paying. Empty for none.</summary>
    public Uri? TermsUrl { get; init; }
}

public sealed class SeedLoginOptions
{
    public string Email { get; init; } = "";

    public string Password { get; init; } = "";
}

/// <summary>The firm's server on the trading platform. The API key is a secret: keep it out of files outside development.</summary>
public sealed class FirmTradingOptions
{
    public string Server { get; init; } = "";

    public string ApiKey { get; init; } = "";

    /// <summary>The trading group new accounts are opened in.</summary>
    public string Group { get; init; } = "";

    /// <summary>The group's account currency. Challenges must be in it.</summary>
    public string Currency { get; init; } = "USD";
}

public sealed class FirmWebhookOptions
{
    /// <summary>Where the firm receives webhooks. Empty for none.</summary>
    public Uri? Url { get; init; }

    /// <summary>Signs every webhook. A secret: keep it out of files outside development.</summary>
    public string Secret { get; init; } = "";
}

public sealed class SeedChallengeOptions
{
    public const string TwoStepTemplate = "TwoStep";

    /// <summary>Tiny targets and no minimum trading days, to try the whole flow quickly. For development only.</summary>
    public const string QuickTestTemplate = "QuickTest";

    public string Template { get; init; } = TwoStepTemplate;

    public string Id { get; init; } = "";

    public decimal InitialBalance { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>What the challenge sells for in the portal, in its currency. Empty when it is not for sale.</summary>
    public decimal? Price { get; init; }
}
