using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OnlineSim.Api;

/// <summary>
/// The mock's routes: endpoint path (relative to <c>/api/</c>) → handler. A handler gets the state and the request
/// and returns the JSON body the real backend would send (HTTP 200). Shapes follow the OpenAPI spec and the
/// live captures: <c>response</c> as int or string per endpoint, decimal-string money, <c>[]</c> for empty maps.
/// Auth, queued failures, 456 and 404 are the transport's job (<see cref="MockOnlineSim"/>), not the handlers'.
/// </summary>
internal static class MockHandlers
{
    /// <summary>Endpoints that answer without a key.</summary>
    internal static readonly HashSet<string> PublicPaths = new HashSet<string> { "getNumbersStats.php", "rent/tariffsRent.php", "getFreeList" };

    internal static readonly Dictionary<string, Func<MockState, MockRequest, JsonNode>> Routes =
        new Dictionary<string, Func<MockState, MockRequest, JsonNode>>
        {
            ["getPrice.php"] = GetPrice,
            ["getNum.php"] = GetNum,
            ["getState.php"] = GetState,
            ["setOperationRevise.php"] = SetOperationRevise,
            ["setOperationOk.php"] = SetOperationOk,
            ["getNumbersStats.php"] = GetNumbersStats,
            ["rent/getRentNum.php"] = GetRentNum,
            ["rent/getRentState.php"] = GetRentState,
            ["rent/extendRentState.php"] = ExtendRentState,
            ["rent/closeRentNum.php"] = CloseRentNum,
            ["rent/tariffsRent.php"] = TariffsRent,
            ["getBalance.php"] = GetBalance,
            ["getProfile.php"] = GetProfile,
            ["profile.php"] = PostProfile,
            ["webhook-logs"] = WebhookLogs,
            ["getPaymentHistory.php"] = GetPaymentHistory,
            ["getFreeList"] = GetFreeList,
        };

    private static readonly Dictionary<int, (string Name, string Locale)> CountryNames = new Dictionary<int, (string, string)>
    {
        [7] = ("russia", "Russia"),
        [44] = ("united_kingdom", "United Kingdom"),
    };

    private static JsonObject Error(string code) => new JsonObject { ["response"] = code };

    private static bool Flag(MockRequest req, string key) => req.Query.TryGetValue(key, out var v) && v != "0";

    private static bool Has(MockRequest req, string key) => req.Query.ContainsKey(key);

    private static long Int(MockRequest req, string key, long fallback) =>
        req.Query.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static string Str(MockRequest req, string key) => req.Query.TryGetValue(key, out var v) ? v : "";

    /// <summary>USD with <paramref name="digits"/> decimals as a string, as the backend sends <c>balance</c> and rent <c>sum</c>.</summary>
    private static string Decimal(long mills, int digits = 3) =>
        MockState.FromMills(mills).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static (string Name, string Locale) CountryName(int code) =>
        CountryNames.TryGetValue(code, out var n) ? n : ("country_" + code.ToString(CultureInfo.InvariantCulture), "Country " + code.ToString(CultureInfo.InvariantCulture));

    private static JsonArray Array(IEnumerable<JsonNode?> items) => new JsonArray(items.ToArray());

    /// <summary>Laravel paginator over <paramref name="items"/>.</summary>
    private static JsonObject Paginate(IReadOnlyList<JsonNode> items, long page, long perPage, string path)
    {
        var total = items.Count;
        var lastPage = Math.Max(1, (long)Math.Ceiling(total / (double)perPage));
        var data = items.Skip((int)((page - 1) * perPage)).Take((int)perPage).ToList();
        string Url(long p) => $"https://onlinesim.host/api/{path}?page={p.ToString(CultureInfo.InvariantCulture)}";
        return new JsonObject
        {
            ["current_page"] = page,
            ["data"] = Array(data),
            ["first_page_url"] = Url(1),
            ["from"] = data.Count > 0 ? (long?)((page - 1) * perPage + 1) : null,
            ["last_page"] = lastPage,
            ["last_page_url"] = Url(lastPage),
            ["links"] = new JsonArray(),
            ["next_page_url"] = page < lastPage ? Url(page + 1) : null,
            ["path"] = $"https://onlinesim.host/api/{path}",
            ["per_page"] = perPage,
            ["prev_page_url"] = page > 1 ? Url(page - 1) : null,
            ["to"] = data.Count > 0 ? (long?)((page - 1) * perPage + data.Count) : null,
            ["total"] = total,
        };
    }

    // ----- numbers -----

    private static string MsgOf(MockSms sms, bool toCode) => toCode ? sms.Code ?? sms.Text : sms.Text;

    private static JsonObject OperationView(MockState state, OperationState op, MockRequest req)
    {
        var toCode = Flag(req, "message_to_code");
        var open = op.Status != OperationStatus.OverOk;
        var view = new JsonObject
        {
            ["country"] = op.Country,
            ["sum"] = op.Sum,
            ["service"] = op.Service,
            ["number"] = op.Number,
            ["subscription_package_id"] = null,
            ["webhook_url"] = state.WebhookUrl,
            ["response"] = op.Status,
            ["tzid"] = op.Tzid,
            ["time"] = open ? MockState.OperationTime : 0,
            ["form"] = "index",
        };
        if (Flag(req, "msg_list"))
        {
            if (op.Messages.Count > 0)
            {
                view["msg"] = Array(op.Messages.Select(s => (JsonNode?)new JsonObject { ["service"] = s.Service, ["msg"] = MsgOf(s, toCode) }));
            }
        }
        else if (op.Cursor < op.Messages.Count)
        {
            view["msg"] = MsgOf(op.Messages[op.Cursor], toCode);
        }
        if (Has(req, "number"))
        {
            view["guard_interval_remaining_seconds"] = open && op.Messages.Count == 0 ? state.GuardIntervalSeconds : 0;
        }
        return view;
    }

    private static JsonNode GetPrice(MockState state, MockRequest req)
    {
        if (!state.Prices.TryGetValue((int)Int(req, "country", 7), out var prices)) return Error("UNDEFINED_COUNTRY");
        return new JsonObject { ["response"] = "1", ["price"] = prices.TryGetValue(Str(req, "service"), out var p) ? (decimal?)p : null };
    }

    private static JsonNode GetNum(MockState state, MockRequest req)
    {
        var country = (int)Int(req, "country", 7);
        var service = Str(req, "service");
        if (!state.Prices.TryGetValue(country, out var prices)) return Error("UNDEFINED_COUNTRY");
        if (!prices.TryGetValue(service, out var price)) return Error("ERROR_NO_SERVICE");
        var cost = MockState.ToMills(price);
        if (state.Available < cost) return Error("WARNING_LOW_BALANCE");

        var number = "+" + country.ToString(CultureInfo.InvariantCulture) + MockState.ShortNumber(state.PeekId());
        var reject = Str(req, "reject").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (reject.Any(prefix => number.Substring(1).StartsWith(prefix, StringComparison.Ordinal))) return Error("NO_NUMBER");

        var tzid = state.NextId();
        state.Frozen += cost;
        state.Operations[tzid] = new OperationState(tzid, service, country, number, price);
        if (!Has(req, "number")) return new JsonObject { ["response"] = 1, ["tzid"] = tzid };
        return new JsonObject
        {
            ["response"] = 1,
            ["tzid"] = tzid,
            ["number"] = number,
            ["country"] = country,
            ["time"] = MockState.OperationTime,
            ["service"] = service,
        };
    }

    private static JsonNode GetState(MockState state, MockRequest req)
    {
        var ops = Has(req, "tzid")
            ? (state.Operations.TryGetValue(Int(req, "tzid", -1), out var one) ? new List<OperationState> { one } : new List<OperationState>())
            : state.Operations.Values.ToList();
        foreach (var op in ops) state.Poll(op.Tzid);
        if (ops.Count == 0) return Error("ERROR_NO_OPERATIONS");
        var sorted = Str(req, "orderby") == "desc" ? ops.OrderByDescending(o => o.Tzid) : ops.OrderBy(o => o.Tzid);
        return Array(sorted.Select(op => (JsonNode?)OperationView(state, op, req)));
    }

    private static JsonNode SetOperationRevise(MockState state, MockRequest req)
    {
        var tzid = Int(req, "tzid", 0);
        // Always succeeds, even for unknown tzids, like the backend.
        if (state.Operations.TryGetValue(tzid, out var op) && op.Status != OperationStatus.OverOk)
        {
            op.Cursor = op.Messages.Count;
            op.Status = OperationStatus.NumWait;
        }
        return new JsonObject { ["response"] = 1, ["tzid"] = tzid };
    }

    private static JsonNode SetOperationOk(MockState state, MockRequest req)
    {
        var tzid = Int(req, "tzid", 0);
        if (!state.Operations.TryGetValue(tzid, out var op) || op.Status == OperationStatus.OverOk) return Error("ERROR_WRONG_TZID");
        var received = op.Messages.Count > 0;
        if (Flag(req, "ban") && received) return new JsonObject { ["response"] = "0" };
        if (!received && state.GuardIntervalSeconds > 0) return Error("NO_COMPLETE_TZID");
        var cost = MockState.ToMills(op.Sum);
        state.Frozen -= cost;
        if (received) state.Balance -= cost;
        op.Status = OperationStatus.OverOk;
        return new JsonObject { ["response"] = 1, ["tzid"] = tzid };
    }

    private static JsonObject CountryStat(MockState state, int code)
    {
        var (name, locale) = CountryName(code);
        var services = new JsonObject();
        var id = 0;
        foreach (var pair in state.Prices.TryGetValue(code, out var prices) ? prices : new Dictionary<string, decimal>())
        {
            services["service_" + pair.Key] = new JsonObject
            {
                ["count"] = 100,
                ["popular"] = false,
                ["code"] = code,
                ["price"] = pair.Value,
                ["id"] = ++id,
                ["service"] = pair.Key,
                ["slug"] = pair.Key,
            };
        }
        return new JsonObject
        {
            ["name"] = name,
            ["position"] = code,
            ["code"] = code,
            ["other"] = false,
            ["new"] = false,
            ["enabled"] = true,
            ["services"] = services.Count > 0 ? services : new JsonArray(),
            ["locale_name"] = locale,
        };
    }

    private static JsonNode GetNumbersStats(MockState state, MockRequest req)
    {
        if (Str(req, "country") == "all")
        {
            var all = new JsonObject();
            foreach (var code in state.Prices.Keys.OrderBy(c => c)) all[code.ToString(CultureInfo.InvariantCulture)] = CountryStat(state, code);
            return all.Count > 0 ? all : new JsonArray();
        }
        var one = (int)Int(req, "country", 7);
        return state.Prices.ContainsKey(one) ? CountryStat(state, one) : Error("NO_COUNTRY");
    }

    // ----- rent -----

    private static JsonObject DaysMap(Dictionary<int, decimal> days)
    {
        var map = new JsonObject();
        foreach (var d in days) map[d.Key.ToString(CultureInfo.InvariantCulture)] = d.Value;
        return map;
    }

    private static JsonObject RentItem(MockState state, RentState rent, MockRequest req)
    {
        state.RentTariffs.TryGetValue(rent.Country, out var tariff);
        var extendable = rent.Extension && tariff?.Extend == 1;
        var start = rent.Tzid;
        var item = new JsonObject
        {
            ["status"] = 1,
            ["extension"] = rent.Extension ? 1 : 0,
            ["messages"] = Array(Enumerable.Reverse(rent.Messages).Select(m => (JsonNode?)new JsonObject
            {
                ["id"] = m.Id,
                ["service"] = m.Service,
                ["text"] = m.Text,
                ["code"] = m.Code ?? "",
                ["created_at"] = m.CreatedAt,
            })),
            ["webhook_url"] = state.WebhookUrl,
            ["sum"] = Decimal(MockState.ToMills(rent.Sum), 2),
            ["country"] = rent.Country,
            ["start_time"] = MockState.Stamp(start),
            ["stop_time"] = MockState.Stamp(start + rent.Days * 86_400L),
            ["checked_time"] = MockState.Stamp(start),
            ["number"] = rent.Number,
            ["days_autoextend"] = 0,
            ["rent"] = 1,
            ["tzid"] = rent.Tzid,
            ["time"] = rent.Days * 24 * 60,
            ["days"] = rent.Days,
            ["hours"] = rent.Days * 24,
            ["extend"] = extendable ? DaysMap(tariff!.Days) : new JsonArray(),
            ["checked"] = true,
            ["reload"] = null,
            ["day_extend"] = extendable && tariff!.Days.TryGetValue(1, out var day) ? day : 0m,
            ["m_ext"] = false,
            ["freeze"] = false,
        };
        if (Has(req, "number")) item["time_seconds"] = rent.Days * 86_400L;
        return item;
    }

    private static JsonNode GetRentNum(MockState state, MockRequest req)
    {
        var country = (int)Int(req, "country", 7);
        if (!state.RentTariffs.TryGetValue(country, out var tariff)) return Error("NO_COUNTRY");
        var days = (int)Int(req, "days", 1);
        if (!tariff.Days.TryGetValue(days, out var price)) return Error("UNDEFINED_DAYS");
        if (state.Available < MockState.ToMills(price)) return Error("WARNING_LOW_BALANCE");
        var tzid = state.NextId();
        state.Balance -= MockState.ToMills(price);
        // `extension` defaults to 1 on the server; `0` makes the rent non-extendable for good.
        var rent = new RentState(tzid, country, MockState.ShortNumber(tzid), days, price, Str(req, "extension") != "0");
        state.Rents[tzid] = rent;
        return new JsonObject { ["response"] = 1, ["item"] = RentItem(state, rent, req) };
    }

    private static JsonNode GetRentState(MockState state, MockRequest req)
    {
        var rents = Has(req, "tzid")
            ? (state.Rents.TryGetValue(Int(req, "tzid", -1), out var one) ? new List<RentState> { one } : new List<RentState>())
            : state.Rents.Values.ToList();
        foreach (var rent in rents) state.Poll(rent.Tzid);
        return new JsonObject { ["response"] = 1, ["list"] = Array(rents.Select(r => (JsonNode?)RentItem(state, r, req))) };
    }

    private static JsonNode ExtendRentState(MockState state, MockRequest req)
    {
        if (!state.Rents.TryGetValue(Int(req, "tzid", 0), out var rent)) return Error("ERROR_WRONG_TZID");
        if (!rent.Extension || !state.RentTariffs.TryGetValue(rent.Country, out var tariff) || tariff.Extend != 1) return Error("TRY_AGAIN_LATER");
        var days = (int)Int(req, "days", 1);
        if (!tariff.Days.TryGetValue(days, out var price)) return Error("UNDEFINED_DAYS");
        if (state.Available < MockState.ToMills(price)) return Error("WARNING_LOW_BALANCE");
        state.Balance -= MockState.ToMills(price);
        rent.Days += days;
        rent.Sum = MockState.FromMills(MockState.ToMills(rent.Sum) + MockState.ToMills(price));
        return new JsonObject { ["response"] = 1, ["item"] = RentItem(state, rent, req) };
    }

    private static JsonNode CloseRentNum(MockState state, MockRequest req)
    {
        var tzid = Int(req, "tzid", 0);
        if (!state.Rents.Remove(tzid)) return Error("ERROR_WRONG_TZID");
        state.Scheduled.Remove(tzid);
        return new JsonObject { ["response"] = "1" };
    }

    private static JsonNode TariffsRent(MockState state, MockRequest req)
    {
        JsonNode Tariff(int code)
        {
            if (!state.RentTariffs.TryGetValue(code, out var t)) return new JsonArray();
            JsonObject Count()
            {
                var count = new JsonObject();
                foreach (var d in t.Days.Keys) count[d.ToString(CultureInfo.InvariantCulture)] = 100;
                return count;
            }
            return new JsonObject
            {
                ["code"] = code,
                ["enabled"] = true,
                ["name"] = CountryName(code).Name,
                ["new"] = false,
                ["position"] = code,
                ["count"] = Count(),
                ["days"] = DaysMap(t.Days),
                ["extend"] = t.Extend,
                ["confirm"] = false,
                ["ext_counts"] = Count(),
            };
        }

        if (Has(req, "country")) return Tariff((int)Int(req, "country", 0));
        var all = new JsonObject();
        foreach (var code in state.RentTariffs.Keys.OrderBy(c => c)) all[code.ToString(CultureInfo.InvariantCulture)] = Tariff(code);
        return all.Count > 0 ? all : new JsonArray();
    }

    // ----- user -----

    private static JsonNode GetBalance(MockState state, MockRequest req)
    {
        var res = new JsonObject { ["response"] = "1", ["balance"] = Decimal(state.Balance), ["zbalance"] = MockState.FromMills(state.Frozen) };
        if (Has(req, "income"))
        {
            res["income"] = 0;
            res["income_usd"] = "0.000";
        }
        return res;
    }

    private static JsonNode GetProfile(MockState state, MockRequest req) => new JsonObject
    {
        ["response"] = "1",
        ["profile"] = new JsonObject
        {
            ["id"] = MockState.UserId,
            ["hash_id"] = "mock",
            ["name"] = "mock@example.com",
            ["username"] = "mock@example.com",
            ["email"] = "mock@example.com",
            ["email_verified_at"] = MockState.Stamp(),
            ["active"] = "true",
            ["apikey"] = state.ApiKey,
            ["api_access"] = true,
            ["is_api"] = true,
            ["number"] = null,
            ["country"] = null,
            ["locale"] = "en",
            ["currency"] = "USD",
            ["number_region"] = null,
            ["number_country"] = null,
            ["number_reject"] = new JsonArray(),
            ["ugroup"] = 1,
            ["verify"] = 0,
            ["block"] = 0,
            ["resetCount"] = null,
            ["created_at"] = MockState.Stamp(),
            ["notification_at"] = null,
            ["first_login"] = false,
            ["sub"] = 0,
            ["disable"] = false,
            ["webhook_url"] = state.WebhookUrl,
            ["is_project_subscription"] = false,
        },
    };

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.Scheme == "http" || url.Scheme == "https") && url.Host.Length > 0;

    private static JsonNode PostProfile(MockState state, MockRequest req)
    {
        JsonElement profile = default;
        var ok = false;
        if (req.Method == "POST" && req.Body != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(req.Body);
                ok = doc.RootElement.ValueKind == JsonValueKind.Object &&
                     doc.RootElement.TryGetProperty("profile", out profile) &&
                     profile.ValueKind == JsonValueKind.Object;
                profile = profile.Clone();
            }
            catch (JsonException)
            {
                ok = false;
            }
        }
        if (!ok) return Error("ERROR_PARAMS");

        if (profile.TryGetProperty("webhook_url", out var url))
        {
            if (url.ValueKind == JsonValueKind.Null) state.WebhookUrl = null;
            else if (url.ValueKind == JsonValueKind.String && IsHttpUrl(url.GetString()!)) state.WebhookUrl = url.GetString();
            else return new JsonObject { ["response"] = new JsonObject { ["webhook_url"] = new JsonArray("The webhook url must be a valid URL.") } };
        }
        return new JsonObject { ["response"] = "1", ["user"] = new JsonObject { ["id"] = MockState.UserId, ["webhook_url"] = state.WebhookUrl } };
    }

    private static JsonNode WebhookLog(WebhookLogEntry log) => new JsonObject
    {
        ["id"] = log.Id,
        ["user_id"] = MockState.UserId,
        ["webhook_url"] = log.Url,
        ["type"] = log.Type,
        ["params"] = new JsonObject
        {
            ["webhook_type"] = log.Type,
            ["operation_id"] = log.Tzid,
            ["user_id"] = MockState.UserId,
            ["country_code"] = log.Country,
            ["number"] = log.Number,
            ["sender"] = log.Sms.Service,
            ["message"] = log.Sms.Text,
            ["code"] = log.Sms.Code,
            ["time_start"] = log.Time,
            ["time_left"] = MockState.OperationTime,
        },
        ["status"] = "success",
        ["error"] = null,
        ["created_at"] = log.Time,
        ["updated_at"] = log.Time,
    };

    private static JsonNode WebhookLogs(MockState state, MockRequest req) => new JsonObject
    {
        ["response"] = "1",
        ["data"] = Paginate(Enumerable.Reverse(state.WebhookLogs).Select(WebhookLog).ToList(), Int(req, "page", 1), 10, "webhook-logs"),
    };

    private static JsonNode GetPaymentHistory(MockState state, MockRequest req) => new JsonObject
    {
        ["response"] = "1",
        ["orders"] = Paginate(new List<JsonNode>(), Int(req, "page", 1), 10, "getPaymentHistory.php"),
        ["paylist"] = new JsonArray(),
        ["domains"] = new JsonArray(),
        ["currency"] = new JsonObject { ["USD"] = 1 },
    };

    // ----- free -----

    private static readonly SortedDictionary<int, (string Name, string Text, Dictionary<string, (string Sender, string Text)[]> Numbers)> Free =
        new SortedDictionary<int, (string, string, Dictionary<string, (string, string)[]>)>
        {
            [44] = ("united_kingdom", "United Kingdom", new Dictionary<string, (string, string)[]>
            {
                ["7459608320"] = new[]
                {
                    ("Amazon", "Amazon: Use 195895 to reset your password."),
                    ("Microsoft", "Microsoft access code: 2991"),
                },
            }),
            [7] = ("russia", "Russia", new Dictionary<string, (string, string)[]>
            {
                ["9001234567"] = new[] { ("Telegram", "Telegram code: 51423") },
            }),
        };

    private static JsonNode GetFreeList(MockState state, MockRequest req)
    {
        var wanted = (int)Int(req, "country", 44);
        var country = Free.ContainsKey(wanted) ? wanted : Free.Keys.First();
        var data = Free[country];
        var numbers = new JsonObject();
        foreach (var n in data.Numbers.Keys)
        {
            numbers[n] = new JsonObject
            {
                ["country"] = country,
                ["country_original"] = data.Name,
                ["data_humans"] = "1 day ago",
                ["full_number"] = country.ToString(CultureInfo.InvariantCulture) + n,
                ["is_archive"] = false,
            };
        }
        var requested = Str(req, "number");
        var number = requested.Length > 0 && data.Numbers.ContainsKey(requested) ? requested : data.Numbers.Keys.First();
        var messages = data.Numbers[number].Select((m, i) => (JsonNode)new JsonObject
        {
            ["id"] = i + 1,
            ["text"] = m.Text,
            ["in_number"] = m.Sender,
            ["my_number"] = number,
            ["created_at"] = MockState.Stamp(i),
            ["data_humans"] = "1 day ago",
            ["code"] = MockState.ExtractCode(m.Text) ?? "-",
        }).ToList();
        var page = Paginate(messages, Int(req, "page", 1), Int(req, "count", 10), "getFreeList");
        page["number"] = number;
        page["country"] = country;
        return new JsonObject
        {
            ["response"] = 1,
            ["countries"] = Array(Free.Select(c => (JsonNode?)new JsonObject
            {
                ["country"] = c.Key,
                ["country_text"] = c.Value.Text,
                ["country_original"] = c.Value.Name,
            })),
            ["numbers"] = numbers,
            ["messages"] = page,
        };
    }
}
