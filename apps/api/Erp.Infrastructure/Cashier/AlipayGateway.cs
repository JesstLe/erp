using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aop.Api;
using Aop.Api.Domain;
using Aop.Api.Request;
using Aop.Api.Util;
using Erp.Domain.Cashier;

namespace Erp.Infrastructure.Cashier;

internal sealed class AlipayGateway(HttpClient httpClient) : IPaymentChannelGateway
{
    public PaymentChannelProvider Provider => PaymentChannelProvider.Alipay;

    public async Task<PaymentChannelQrResult> CreateQrAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var model = new AlipayTradePrecreateModel
            {
                OutTradeNo = request.OutTradeNo,
                TotalAmount = ToYuan(request.AmountMinor),
                Subject = request.Subject,
                QrCodeTimeoutExpress = $"{Math.Max(1, (int)Math.Ceiling((request.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes))}m",
            };
            var sdkRequest = new AlipayTradePrecreateRequest();
            sdkRequest.SetBizModel(model);
            sdkRequest.SetNotifyUrl(credentials.NotifyUrl);
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            if (response.Code != "10000" || string.IsNullOrWhiteSpace(response.QrCode))
                return new PaymentChannelQrResult(false, null, AlipayError(response.SubCode, response.Code),
                    response.SubMsg ?? response.Msg ?? "支付宝预下单失败");
            if (!string.Equals(response.OutTradeNo, request.OutTradeNo, StringComparison.Ordinal))
                return new PaymentChannelQrResult(false, null, "CHANNEL_RESULT_CONFLICT",
                    "支付宝预下单结果与本地订单不一致");
            return new PaymentChannelQrResult(true, response.QrCode, null, null);
        }
        catch (Exception exception) when (IsChannelException(exception))
        {
            return new PaymentChannelQrResult(false, null, "CHANNEL_UNAVAILABLE", "支付宝暂时不可用");
        }
    }

    public async Task<PaymentChannelQueryResult> QueryAsync(PaymentChannelCredentialProfile credentials,
        string outTradeNo, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var sdkRequest = new AlipayTradeQueryRequest();
            sdkRequest.SetBizModel(new AlipayTradeQueryModel { OutTradeNo = outTradeNo });
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            if (response.Code != "10000")
            {
                if (response.SubCode == "ACQ.TRADE_NOT_EXIST")
                    return new PaymentChannelQueryResult(true, PaymentChannelTradeState.Pending, null, null, null,
                        null, null);
                return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                    AlipayError(response.SubCode, response.Code), response.SubMsg ?? response.Msg ?? "支付宝查单失败");
            }
            if (!string.Equals(response.OutTradeNo, outTradeNo, StringComparison.Ordinal) ||
                !TryMinor(response.TotalAmount, out var amountMinor))
                return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                    "CHANNEL_RESULT_CONFLICT", "支付宝查单结果与本地订单不一致");
            DateTimeOffset? paidAt = DateTime.TryParseExact(response.SendPayDate, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedPaidAt)
                ? new DateTimeOffset(parsedPaidAt) : null;
            return new PaymentChannelQueryResult(true, MapTradeState(response.TradeStatus), response.TradeNo,
                amountMinor, paidAt, null, null);
        }
        catch (Exception exception) when (IsChannelException(exception))
        {
            return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                "CHANNEL_UNAVAILABLE", "支付宝暂时不可用");
        }
    }

    public async Task<PaymentChannelCloseResult> CloseAsync(PaymentChannelCredentialProfile credentials,
        string outTradeNo, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var sdkRequest = new AlipayTradeCloseRequest();
            sdkRequest.SetBizModel(new AlipayTradeCloseModel { OutTradeNo = outTradeNo });
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            return response.Code == "10000"
                ? new PaymentChannelCloseResult(true, PaymentChannelTradeState.Closed, null, null)
                : new PaymentChannelCloseResult(false, PaymentChannelTradeState.Unknown,
                    AlipayError(response.SubCode, response.Code), response.SubMsg ?? response.Msg ?? "支付宝关单失败");
        }
        catch (Exception exception) when (IsChannelException(exception))
        {
            return new PaymentChannelCloseResult(false, PaymentChannelTradeState.Unknown, "CHANNEL_UNAVAILABLE",
                "支付宝暂时不可用");
        }
    }

    public async Task<PaymentChannelRefundResult> RefundAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelRefundRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var sdkRequest = new AlipayTradeRefundRequest();
            sdkRequest.SetBizModel(new AlipayTradeRefundModel
            {
                OutTradeNo = request.OutTradeNo,
                OutRequestNo = request.OutRefundNo,
                RefundAmount = ToYuan(request.RefundAmountMinor),
                RefundReason = request.Reason.Length <= 200 ? request.Reason : request.Reason[..200],
            });
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            if (response.Code != "10000")
                return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                    AlipayError(response.SubCode, response.Code), response.SubMsg ?? response.Msg ?? "支付宝退款失败");
            if (!string.Equals(response.OutTradeNo, request.OutTradeNo, StringComparison.Ordinal) ||
                !TryMinor(response.RefundFee, out var refundAmount) || refundAmount != request.RefundAmountMinor)
                return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                    "CHANNEL_RESULT_CONFLICT", "支付宝退款结果与本地退款单或金额不一致");
            var state = response.FundChange == "Y"
                ? PaymentChannelRefundState.Succeeded : PaymentChannelRefundState.Pending;
            return new PaymentChannelRefundResult(true, state, response.RefundSettlementId,
                refundAmount, null, null);
        }
        catch (Exception exception) when (IsChannelException(exception))
        {
            return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                "CHANNEL_UNAVAILABLE", "支付宝退款暂时不可用");
        }
    }

    public async Task<PaymentChannelRefundResult> QueryRefundAsync(
        PaymentChannelCredentialProfile credentials, PaymentChannelRefundRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var sdkRequest = new AlipayTradeFastpayRefundQueryRequest();
            sdkRequest.SetBizModel(new AlipayTradeFastpayRefundQueryModel
            {
                OutTradeNo = request.OutTradeNo,
                OutRequestNo = request.OutRefundNo,
            });
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            if (response.Code != "10000")
                return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                    AlipayError(response.SubCode, response.Code), response.SubMsg ?? response.Msg ?? "支付宝退款查询失败");
            if (!string.Equals(response.OutTradeNo, request.OutTradeNo, StringComparison.Ordinal) ||
                !string.Equals(response.OutRequestNo, request.OutRefundNo, StringComparison.Ordinal) ||
                !TryMinor(response.RefundAmount, out var refundAmount) ||
                refundAmount != request.RefundAmountMinor)
                return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                    "CHANNEL_RESULT_CONFLICT", "支付宝退款查询结果与本地退款单或金额不一致");
            var succeeded = response.RefundStatus == "REFUND_SUCCESS" ||
                !string.IsNullOrWhiteSpace(response.GmtRefundPay);
            return new PaymentChannelRefundResult(true,
                succeeded ? PaymentChannelRefundState.Succeeded : PaymentChannelRefundState.Pending,
                response.RefundSettlementId, refundAmount, null, null);
        }
        catch (Exception exception) when (IsChannelException(exception))
        {
            return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                "CHANNEL_UNAVAILABLE", "支付宝退款查询暂时不可用");
        }
    }

    public async Task<PaymentChannelBillResult> DownloadBillAsync(
        PaymentChannelCredentialProfile credentials, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(credentials, cancellationToken);
            var sdkRequest = new AlipayDataDataserviceBillDownloadurlQueryRequest();
            sdkRequest.SetBizModel(new AlipayDataDataserviceBillDownloadurlQueryModel
            {
                BillType = "trade",
                BillDate = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
            var response = await Task.Run(() => client.Execute(sdkRequest), cancellationToken);
            if (response.Code != "10000")
                return BillFailure(AlipayError(response.SubCode, response.Code),
                    response.SubMsg ?? response.Msg ?? "支付宝账单地址查询失败");
            if (!TryAlipayBillUri(response.BillDownloadUrl, out var uri))
                return BillFailure("CHANNEL_BILL_INVALID_RESPONSE", "支付宝账单下载地址无效");
            var archiveBytes = await DownloadBoundedAsync(uri!, 20 * 1024 * 1024, cancellationToken);
            var csvBytes = ExtractCsv(archiveBytes, 20 * 1024 * 1024);
            var content = DecodeBill(csvBytes);
            var entries = PaymentChannelBillCsv.ParseAlipay(content);
            return new PaymentChannelBillResult(true, entries, SHA256.HashData(archiveBytes), null, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            TaskCanceledException or InvalidDataException or DecoderFallbackException or FormatException or
            CryptographicException or InvalidOperationException or AopException)
        {
            return BillFailure("CHANNEL_BILL_UNAVAILABLE", "支付宝账单暂时不可用或格式无效");
        }
    }

    public PaymentChannelNotification VerifyNotification(PaymentChannelCredentialProfile credentials,
        PaymentChannelNotificationEnvelope notification)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(notification.Body));
        try
        {
            if (notification.Form is null || notification.Form.Count == 0)
                return Invalid(digest, "CHANNEL_NOTIFICATION_INVALID");
            var publicKey = File.ReadAllText(credentials.CounterpartyPublicKeyPath).Trim();
            var parameters = notification.Form.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (!AlipaySignature.RSACheckV1(parameters, publicKey, "UTF-8", "RSA2", false))
                return Invalid(digest, "CHANNEL_SIGNATURE_INVALID");
            if (!parameters.TryGetValue("app_id", out var appId) ||
                !string.Equals(appId, credentials.AppId, StringComparison.Ordinal))
                return Invalid(digest, "CHANNEL_MERCHANT_MISMATCH");
            if (!parameters.TryGetValue("out_trade_no", out var outTradeNo) ||
                !parameters.TryGetValue("trade_status", out var tradeStatus) ||
                !parameters.TryGetValue("total_amount", out var totalAmount) ||
                !TryMinor(totalAmount, out var amountMinor))
                return Invalid(digest, "CHANNEL_NOTIFICATION_INVALID");
            var eventId = parameters.GetValueOrDefault("notify_id");
            if (string.IsNullOrWhiteSpace(eventId)) eventId = Convert.ToHexString(digest);
            DateTimeOffset? paidAt = parameters.TryGetValue("gmt_payment", out var paidText) &&
                DateTime.TryParseExact(paidText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsedPaidAt) ? new DateTimeOffset(parsedPaidAt) : null;
            return new PaymentChannelNotification(true, eventId, tradeStatus, outTradeNo,
                parameters.GetValueOrDefault("trade_no"), MapTradeState(tradeStatus), amountMinor, paidAt, digest,
                null);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or FormatException or
            ArgumentException)
        {
            return Invalid(digest, "CHANNEL_NOTIFICATION_INVALID");
        }
    }

    private static async Task<DefaultAopClient> CreateClient(PaymentChannelCredentialProfile credentials,
        CancellationToken cancellationToken)
    {
        var privateKey = (await File.ReadAllTextAsync(credentials.MerchantPrivateKeyPath, cancellationToken)).Trim();
        var alipayPublicKey = (await File.ReadAllTextAsync(credentials.CounterpartyPublicKeyPath,
            cancellationToken)).Trim();
        var client = new DefaultAopClient(credentials.GatewayUrl!, credentials.AppId, privateKey, "json", "1.0",
            "RSA2", alipayPublicKey, "UTF-8", false);
        client.SetTimeout(10_000);
        return client;
    }

    private static string ToYuan(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryMinor(string? yuan, out long minor)
    {
        minor = 0;
        return decimal.TryParse(yuan, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) &&
            value >= 0 && value <= 100_000_000m && value == decimal.Round(value, 2) &&
            TryConvert(value, out minor);
    }

    private static bool TryConvert(decimal value, out long minor)
    {
        try { minor = checked((long)(value * 100m)); return true; }
        catch (OverflowException) { minor = 0; return false; }
    }

    private static PaymentChannelTradeState MapTradeState(string? state) => state switch
    {
        "TRADE_SUCCESS" or "TRADE_FINISHED" => PaymentChannelTradeState.Paid,
        "WAIT_BUYER_PAY" => PaymentChannelTradeState.Pending,
        "TRADE_CLOSED" => PaymentChannelTradeState.Closed,
        _ => PaymentChannelTradeState.Unknown,
    };

    private static string AlipayError(string? subCode, string? code) =>
        $"ALIPAY_{subCode ?? code ?? "UNKNOWN"}".Replace('.', '_').ToUpperInvariant();

    private static bool IsChannelException(Exception exception) => exception is IOException or TaskCanceledException or
        CryptographicException or InvalidOperationException or AopException;

    private static PaymentChannelNotification Invalid(byte[] digest, string code) =>
        new(false, null, null, null, null, PaymentChannelTradeState.Unknown, null, null, digest, code);

    private static bool TryAlipayBillUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            parsed.UserInfo.Length > 0 || parsed.Host is not ("dwbillcenter.alipay.com" or
                "dwbillcenter.alipaydev.com"))
            return false;
        uri = parsed;
        return true;
    }

    private async Task<byte[]> DownloadBoundedAsync(Uri uri, int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("支付宝账单文件超过大小限制");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (target.Length + read > maximumBytes)
                throw new InvalidDataException("支付宝账单文件超过大小限制");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static byte[] ExtractCsv(byte[] archiveBytes, int maximumBytes)
    {
        if (archiveBytes.Length < 4 || archiveBytes[0] != (byte)'P' || archiveBytes[1] != (byte)'K')
            return archiveBytes;
        using var source = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        var candidates = archive.Entries.Where(entry => entry.Name.EndsWith(".csv",
                StringComparison.OrdinalIgnoreCase) && !entry.Name.Contains("汇总", StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Name.Contains("业务明细", StringComparison.Ordinal))
            .ThenByDescending(entry => entry.Length).ToList();
        var selected = candidates.FirstOrDefault() ??
            throw new InvalidDataException("支付宝账单压缩包缺少交易明细CSV");
        if (selected.Length > maximumBytes)
            throw new InvalidDataException("支付宝账单明细超过大小限制");
        using var target = new MemoryStream();
        using var entryStream = selected.Open();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = entryStream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (target.Length + read > maximumBytes)
                throw new InvalidDataException("支付宝账单明细超过大小限制");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }

    private static string DecodeBill(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetString(content);
        }
    }

    private static PaymentChannelBillResult BillFailure(string? code, string? message) =>
        new(false, [], null, code ?? "CHANNEL_BILL_UNAVAILABLE", message ?? "支付宝账单暂时不可用");
}
