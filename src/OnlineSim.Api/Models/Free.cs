using System.Collections.Generic;

namespace OnlineSim.Api;

/// <summary>A country with free numbers.</summary>
public sealed class FreeCountry
{
    /// <summary>Country code.</summary>
    public int Country { get; init; }

    /// <summary>Localised name.</summary>
    public string CountryText { get; init; } = "";

    /// <summary>Name (slug).</summary>
    public string CountryOriginal { get; init; } = "";
}

/// <summary>A free number.</summary>
public sealed class FreeNumber
{
    /// <summary>Country code.</summary>
    public int Country { get; init; }

    /// <summary>Country name (slug).</summary>
    public string CountryOriginal { get; init; } = "";

    /// <summary>Number with the country code, without <c>+</c>.</summary>
    public string FullNumber { get; init; } = "";

    /// <summary>Age of the last SMS, human readable.</summary>
    public string DataHumans { get; init; } = "";

    /// <summary>Archived number.</summary>
    public bool IsArchive { get; init; }
}

/// <summary>An SMS of a free number.</summary>
public sealed class FreeMessage
{
    /// <summary>Message id.</summary>
    public long Id { get; init; }

    /// <summary>Full text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Sender.</summary>
    public string InNumber { get; init; } = "";

    /// <summary>The free number (short form).</summary>
    public string MyNumber { get; init; } = "";

    /// <summary>Code; <c>-</c> when none was found.</summary>
    public string Code { get; init; } = "";

    /// <summary>Arrival time.</summary>
    public string CreatedAt { get; init; } = "";

    /// <summary>Age, human readable.</summary>
    public string DataHumans { get; init; } = "";
}

/// <summary>SMS page of the selected free number.</summary>
public sealed class FreeMessages : Paginator<FreeMessage>
{
    /// <summary>The selected number (short form).</summary>
    public string? Number { get; init; }

    /// <summary>The selected country.</summary>
    public int Country { get; init; }
}

/// <summary>Result of <see cref="FreeApi.ListAsync"/>.</summary>
public sealed class FreeList
{
    /// <summary>Countries with free numbers.</summary>
    public IReadOnlyList<FreeCountry> Countries { get; init; } = new List<FreeCountry>();

    /// <summary>Numbers of the selected country, keyed by the number without the country code.</summary>
    public IReadOnlyDictionary<string, FreeNumber> Numbers { get; init; } = new Dictionary<string, FreeNumber>();

    /// <summary>SMS of the selected number.</summary>
    public FreeMessages Messages { get; init; } = new FreeMessages();
}
