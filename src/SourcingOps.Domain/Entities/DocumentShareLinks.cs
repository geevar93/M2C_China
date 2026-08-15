namespace SourcingOps.Domain.Entities;

/// <summary>
/// E9-10 (FR-WA-02/FR-WA-03): a temporary, unauthenticated, expiring public link to one stored
/// document, minted so a `wa.me` click-to-chat message can carry the file itself rather than
/// only a note telling staff to attach it by hand.
///
/// This is a **deliberate, narrow exception** to TECH_SPEC §8 / E6-04's "documents are served
/// only through a permission-checked download endpoint — never from a public static path". The
/// exception is scoped by four properties of this row and nothing else:
/// <list type="bullet">
///   <item>the token is 256 bits of CSPRNG output, so the URL is unguessable — it is the
///     capability, not an identifier;</item>
///   <item><see cref="TokenHash"/> stores only a SHA-256 digest, never the token itself, so a
///     leaked backup or a stray query result yields no working link. Same precedent as
///     <see cref="RefreshToken"/>, which has stored hashed since E1-07;</item>
///   <item><see cref="ExpiresAt"/> bounds the window (default 48h — see
///     <c>DispatchOptions.ShareLinkLifetimeHours</c> for why that number);</item>
///   <item><see cref="RevokedAt"/> allows killing a link before it expires.</item>
/// </list>
///
/// The link expiring never touches the underlying file: the source document stays in
/// <c>IFileStorage</c> and remains downloadable through its normal authenticated endpoint. Only
/// the anonymous access path lapses.
/// </summary>
public class DocumentShareLink
{
    public Guid Id { get; set; }

    /// <summary>
    /// Hex-encoded SHA-256 of the issued token. Lookup is by hash, so the raw token exists only
    /// in the response that mints it and in the WhatsApp message the staff member sends.
    /// A plain hash (not a password KDF) is correct here: the input is 256 bits of uniform
    /// randomness, so there is no dictionary to run and nothing for work-factor stretching to buy.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="Constants.DocumentShareTargetTypes"/>. See <see cref="TargetId"/>.</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// The id of the shared row within <see cref="TargetType"/>. Deliberately **not** an FK:
    /// the target is polymorphic across tables that share no base table, and modelling it as one
    /// nullable FK column per shareable type (the shape <see cref="Dispatch"/> uses for its two)
    /// would need a migration for every future shareable document type. The resolver in
    /// <c>DocumentShareLinkService</c> is the single place that maps type+id back to a file, and
    /// a dangling id resolves to the same uniform 404 an unknown token does.
    /// </summary>
    public Guid TargetId { get; set; }

    /// <summary>The staff member who minted the link. Never anonymous — minting requires <c>Dispatch.Send</c>.</summary>
    public Guid CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Null while the link is live. Set by an explicit revoke; never cleared.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Incremented on every successful anonymous fetch. Repeat opens are expected and allowed —
    /// see <c>DocumentShareLinkService</c> for why the link is deliberately not single-use.
    /// </summary>
    public int AccessCount { get; set; }

    public DateTime? LastAccessedAt { get; set; }
}
