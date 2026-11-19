using System;
using System.Collections.Generic;

namespace OnlineSim.Api;

/// <summary>Base class of every exception thrown by the SDK.</summary>
public class OnlineSimException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OnlineSimException(string message) : base(message) { }

    /// <summary>Creates the exception with an inner exception.</summary>
    public OnlineSimException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>The API answered with an error code in <c>response</c> (also a wrong key, HTTP 456).</summary>
public class ApiException : OnlineSimException
{
    /// <summary>Creates the exception. <paramref name="message"/> falls back to <see cref="ErrorMessages"/>, then to the code.</summary>
    public ApiException(string code, string? message = null) : base(Describe(code, message))
    {
        Code = code;
    }

    /// <summary>Raw error code, e.g. <c>ERROR_WRONG_KEY</c>.</summary>
    public string Code { get; }

    private static string Describe(string code, string? message)
    {
        if (!string.IsNullOrEmpty(message)) return message!;
        return ErrorMessages.All.TryGetValue(code, out var text) ? text : code;
    }
}

/// <summary>No number is available right now (<c>NO_NUMBER</c>, <c>NO_NUMBER_FOR_FORWARD</c>). Retry later or pick another country.</summary>
public sealed class NoNumberException : ApiException
{
    /// <summary>Creates the exception.</summary>
    public NoNumberException(string code, string? message = null) : base(code, message) { }
}

/// <summary>Network failure, request timeout, or a response that is not JSON.</summary>
public sealed class TransportException : OnlineSimException
{
    /// <summary>Creates the exception.</summary>
    public TransportException(string message, int? status = null, string body = "", Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        Body = body;
    }

    /// <summary>HTTP status, or <c>null</c> when no response arrived (network error, timeout).</summary>
    public int? Status { get; }

    /// <summary>Start of the response body (up to 500 chars), empty when none.</summary>
    public string Body { get; }
}

/// <summary><see cref="NumbersApi.WaitCodeAsync"/> gave up: no code arrived within its timeout.</summary>
public sealed class WaitTimeoutException : OnlineSimException
{
    /// <summary>Creates the exception.</summary>
    public WaitTimeoutException(string message) : base(message) { }
}

/// <summary>Fallback messages for error codes the server sends without a <c>message</c>.</summary>
public static class ErrorMessages
{
    /// <summary>Error code → short English description.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        ["ACCOUNT_BLOCKED"] = "Account is blocked",
        ["ERROR_WRONG_KEY"] = "Wrong API key",
        ["ERROR_NO_KEY"] = "API key is missing",
        ["ERROR_NO_SERVICE"] = "Service is not specified",
        ["API_ACCESS_DISABLED"] = "API access is disabled in the profile",
        ["API_ACCESS_IP"] = "API access from this IP is not allowed in the profile",
        ["ERROR_NO_TZID"] = "tzid is not specified",
        ["ERROR_WRONG_TZID"] = "Wrong tzid",
        ["ERROR_NO_OPERATIONS"] = "No operations",
        ["NO_NUMBER"] = "No numbers available for this service right now",
        ["NO_NUMBER_FOR_FORWARD"] = "No numbers available for forwarding",
        ["NO_COUNTRY"] = "Country not found",
        ["UNDEFINED_COUNTRY"] = "Unknown country",
        ["WARNING_LOW_BALANCE"] = "Not enough balance",
        ["EXCEEDED_CONCURRENT_OPERATIONS"] = "Too many concurrent operations for this account",
        ["INTERVAL_CONCURRENT_REQUESTS_ERROR"] = "Too many concurrent requests, try again in a few seconds",
        ["TRY_AGAIN_LATER"] = "Temporarily unable to perform the request, try again later",
        ["ACCOUNT_IDENTIFICATION_REQUIRED"] = "Account identification is required",
        ["ERROR_PARAMS"] = "Wrong parameters",
        ["API_DISABLED"] = "API is disabled",
        ["NO_COMPLETE_TZID"] = "The operation cannot be closed yet",
        ["NO_CLOSE_RENT_US"] = "US rent numbers cannot be closed early",
        ["UNDEFINED_DAYS"] = "Unknown rent period",
        ["VALIDATION_ERROR"] = "Validation failed",
        ["TZ_OVER_OK"] = "Operation finished without a new code",
        ["ERROR_NO_ITEMS"] = "Operation failed",
    };
}
