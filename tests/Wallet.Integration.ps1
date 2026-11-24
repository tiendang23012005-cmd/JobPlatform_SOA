param(
    [string]$GatewayUrl = 'http://localhost:5500',
    [string]$DirectPaymentUrl = 'http://localhost:5504'
)

$ErrorActionPreference = 'Stop'
if ($env:PAYMENT_SIMULATOR_SIGNING_KEY.Length -lt 32 -or $env:JWT_KEY.Length -lt 32) {
    throw 'Cần khóa JWT và khóa callback dành riêng cho stack kiểm thử.'
}
Add-Type -AssemblyName System.Net.Http
$Http = [Net.Http.HttpClient]::new()
# Container Payment được tạo lại khi chuyển giữa Test và Production.
$Http.DefaultRequestHeaders.ConnectionClose = $true
$script:checks = 0

function Compose {
    & docker compose -p jobplatform-wallet-test -f docker-compose.yml -f tests/docker-compose.wallet-test.yml @args
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose lỗi (mã $LASTEXITCODE)." }
}

function Sql($Query) {
    $sqlScript = "SET NOCOUNT ON;" + [Environment]::NewLine + "GO" + [Environment]::NewLine + $Query +
        [Environment]::NewLine + "GO" + [Environment]::NewLine
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($sqlScript))
    $command = 'set -f; IFS=; echo $1 | base64 -d | /opt/mssql-tools18/bin/sqlcmd -f 65001 -C -b -W -h -1 -S localhost -U sa -P $MSSQL_SA_PASSWORD -d PaymentDb'
    return ((Compose exec -T sqlserver bash -c $command _ $encoded) | Out-String).Trim()
}

function Request($Method, $Path, $Token, $Body, $Signature) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "$GatewayUrl/$Path")
    if ($Token) { $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token) }
    if ($null -ne $Body) {
        $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 10 -Compress), [Text.Encoding]::UTF8, 'application/json')
    }
    if ($Signature) { $request.Headers.Add('X-Wallet-Signature', $Signature) }
    return $request
}

function Response($Response) {
    try {
        $text = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $data = $null
        if ($text -and $Response.Content.Headers.ContentType.MediaType -like '*json*') {
            $data = ConvertFrom-Json -InputObject $text
        }
        return [pscustomobject]@{ Status = [int]$Response.StatusCode; Data = $data; RetryAfter = $Response.Headers.RetryAfter }
    }
    finally { $Response.Dispose() }
}

function Send($Method, $Path, $Token, $Body, $Signature) {
    $request = Request $Method $Path $Token $Body $Signature
    try { return Response ($Http.SendAsync($request).GetAwaiter().GetResult()) }
    finally { $request.Dispose() }
}

function Send-Parallel($Method, $Path, $Token, $Body, $Signature, $OtherBody = $Body, $OtherSignature = $Signature) {
    $first = Request $Method $Path $Token $Body $Signature
    $second = Request $Method $Path $Token $OtherBody $OtherSignature
    try {
        $one = $Http.SendAsync($first)
        $two = $Http.SendAsync($second)
        Response ($one.GetAwaiter().GetResult())
        Response ($two.GetAwaiter().GetResult())
    }
    finally { $first.Dispose(); $second.Dispose() }
}

function Check($Condition, $Name) {
    if (!$Condition) { throw $Name }
    $script:checks++
    Write-Host "PASS $Name"
}

function Status($Response, $Expected, $Name) {
    Check ($Response.Status -eq $Expected) "$Name (HTTP $($Response.Status), mong đợi $Expected)"
}

function New-TestUser($Role) {
    $email = "wallet-$([guid]::NewGuid().ToString('N'))@example.test"
    $login = @{ email = $email; password = "Wallet@9$([guid]::NewGuid().ToString('N'))" }
    Status (Send POST 'api/v1/auth/register' $null ($login + @{ fullName = "Kiểm thử ví $Role"; role = $Role })) 201 "Đăng ký $Role"
    $otp = ''
    for ($i = 0; $i -lt 25; $i++) {
        $line = Compose logs --since 3m --no-color notification-api |
            Where-Object { $_.Contains('[EMAIL-DEV]') -and $_.Contains($email) } | Select-Object -Last 1
        if ($line) { $otp = [regex]::Match($line, '\b\d{6}\b').Value }
        if ($otp) { break }
        Start-Sleep -Seconds 1
    }
    if (!$otp) { throw 'Không lấy được OTP từ Notification của stack kiểm thử.' }
    Status (Send POST 'api/v1/auth/verify' $null @{ email = $email; otp = $otp }) 200 "Xác minh OTP $Role"
    $authenticated = Send POST 'api/v1/auth/login' $null $login
    Status $authenticated 200 "Đăng nhập $Role qua Gateway"
    return $authenticated.Data
}

function Hmac($Secret, $Text) {
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
    try { return ,$hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)) }
    finally { $hmac.Dispose() }
}

function Sign-Callback($Callback) {
    $fields = [string[]]@(([guid]$Callback.topUpId).ToString('D'), ([guid]$Callback.providerTransactionId).ToString('D'),
        ([decimal]$Callback.amount).ToString('G29', [Globalization.CultureInfo]::InvariantCulture),
        $Callback.currency, $Callback.idempotencyKey, $Callback.status)
    $hash = Hmac $env:PAYMENT_SIMULATOR_SIGNING_KEY (ConvertTo-Json -InputObject $fields -Compress)
    return [BitConverter]::ToString($hash).Replace('-', '')
}

function New-Callback($Id, $Amount, $Key, $Status = 'SUCCESS') {
    $providerId = Sql "SELECT ProviderTransactionId FROM dbo.TopUpPayments WHERE Id = '$Id';"
    return @{ topUpId = $Id; providerTransactionId = $providerId; amount = $Amount; currency = 'VND'; idempotencyKey = $Key; status = $Status }
}

function Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Expired-Token($User) {
    $now = [DateTimeOffset]::UtcNow
    $payload = @{ sub = $User.userId; role = $User.role; email_verified = 'true'; iss = 'JobPlatform';
        aud = 'JobPlatformClients'; nbf = $now.AddMinutes(-3).ToUnixTimeSeconds(); exp = $now.AddMinutes(-1).ToUnixTimeSeconds() }
    $head = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $body = Base64Url ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress)))
    return "$head.$body.$(Base64Url (Hmac $env:JWT_KEY "$head.$body"))"
}

try {
    $employer = New-TestUser Employer
    $other = New-TestUser Freelancer
    $end = 'api/v1/wallet'
    $callbackPath = "$end/top-up/callback"
    $key = "wallet-$([guid]::NewGuid().ToString('N'))"

    Status (Send GET "$end/me") 401 'Ví yêu cầu JWT'
    Status (Send GET "$end/me" 'token-sai') 401 'Từ chối JWT không hợp lệ'
    Status (Send GET "$end/me" (Expired-Token $employer)) 401 'Từ chối JWT hết hạn'
    $opened = @(Send-Parallel GET "$end/me" $employer.token)
    Check ($opened.Count -eq 2 -and $opened[0].Status -eq 200 -and $opened[1].Status -eq 200 -and
        $opened[0].Data.id -eq $opened[1].Data.id) 'Hai request mở ví đồng thời chỉ tạo một ví'
    $walletId = $opened[0].Data.id
    Check ((Sql "SELECT COUNT(*) FROM dbo.Wallets WHERE UserId = '$($employer.userId)';") -eq '1') 'SQL chỉ lưu một ví cho UserId'
    Check ($opened[0].Data.balance -eq 0 -and $opened[0].Data.lockedBalance -eq 0 -and $opened[0].Data.canTopUp) 'Ví mới có hai số dư bằng 0 và cho phép mô phỏng trong Test'
    Check ((Send GET "$end/me?userId=$($employer.userId)" $other.token).Data.userId -eq $other.userId) 'Ví lấy chủ từ JWT'

    foreach ($amount in @(10000, 49999, 0, -50000, 50000.5, 1000000001)) {
        Status (Send POST "$end/top-up" $employer.token @{amount = $amount; idempotencyKey = $key}) 400 "Từ chối số tiền $amount"
    }
    Status (Send POST "$end/top-up" $employer.token @{amount = 100000}) 400 'Mã yêu cầu bắt buộc'

    $start = @{ amount = 100000; idempotencyKey = $key }
    $starts = @(Send-Parallel POST "$end/top-up" $employer.token $start)
    Check ($starts[0].Status -eq 200 -and $starts[1].Status -eq 200 -and
        $starts[0].Data.id -eq $starts[1].Data.id -and $starts[0].Data.status -eq 'Pending') 'Hai request cùng mã nhận cùng một yêu cầu Pending'
    $id = $starts[0].Data.id
    Check ((Send GET "$end/me" $employer.token).Data.balance -eq 0 -and
        (Send GET "$end/transactions" $employer.token).Data.totalCount -eq 0) 'Pending chưa tăng tiền hoặc ghi sổ'
    Status (Send POST "$end/top-up" $employer.token @{amount = 110000; idempotencyKey = $key}) 409 'Cùng mã khác số tiền bị từ chối'
    Status (Send GET "$end/top-up/$id" $other.token) 404 'Không đọc được yêu cầu nạp của ví khác'
    Status (Send POST "$end/mock-payments/$id/complete" $other.token @{success = $true}) 404 'Không điều khiển được cổng của ví khác'

    $callback = New-Callback $id 100000 $key
    $valid = Sign-Callback $callback
    Status (Send POST $callbackPath $null $callback ('0' * 64)) 401 'Từ chối chữ ký sai'
    $bad = $callback.Clone()
    $bad.amount = 100001
    Status (Send POST $callbackPath $null $bad (Sign-Callback $bad)) 400 'Callback có chữ ký nhưng sai số tiền bị từ chối'
    $bad = $callback.Clone()
    $bad.providerTransactionId = [guid]::NewGuid().ToString()
    Status (Send POST $callbackPath $null $bad (Sign-Callback $bad)) 400 'Từ chối sai mã giao dịch cổng'
    $bad = $callback.Clone()
    $bad.idempotencyKey = "$key-khac"
    Status (Send POST $callbackPath $null $bad (Sign-Callback $bad)) 400 'Từ chối sai IdempotencyKey'
    Check ((Send GET "$end/top-up/$id" $employer.token).Data.status -eq 'Pending' -and
        (Send GET "$end/me" $employer.token).Data.balance -eq 0) 'Callback sai giữ Pending và số dư bằng 0'
    $logs = Compose logs --since 2m --no-color payment-api
    Check (@($logs -match 'Callback nạp tiền sai chữ ký').Count -ge 1) 'Callback sai chữ ký được ghi log'

    $settled = Send POST "$end/mock-payments/$id/complete" $employer.token @{success = $true}
    Status $settled 200 'Cổng mô phỏng thực hiện callback có chữ ký'
    Check ($settled.Data.status -eq 'SUCCESS' -and (Send GET "$end/me" $employer.token).Data.balance -eq 100000) 'Nạp 100.000 VNĐ sau callback'
    $replay = Send POST $callbackPath $null $callback $valid
    Check ($replay.Status -eq 200 -and $replay.Data.completedAt -eq $settled.Data.completedAt) 'Callback lặp trả kết quả cũ'
    $lateFailure = New-Callback $id 100000 $key FAILED
    Check ((Send POST $callbackPath $null $lateFailure (Sign-Callback $lateFailure)).Data.status -eq 'SUCCESS') 'Callback thất bại đến sau SUCCESS giữ kết quả cũ'
    $postCommit = Send POST "$end/top-up" $employer.token $start
    Check ($postCommit.Data.id -eq $id -and $postCommit.Data.status -eq 'SUCCESS' -and
        (Send GET "$end/me" $employer.token).Data.balance -eq 100000) 'Retry request sau commit trả SUCCESS, không tăng tiền'

    $failedPayment = (Send POST "$end/top-up" $employer.token @{amount = 50000; idempotencyKey = "$key-failed"}).Data
    $failedResult = Send POST "$end/mock-payments/$($failedPayment.id)/complete" $employer.token @{success = $false}
    Check ($failedResult.Status -eq 200 -and $failedResult.Data.status -eq 'FAILED') 'Hủy tại cổng chuyển FAILED'
    $lateSuccess = New-Callback $failedPayment.id 50000 "$key-failed"
    Check ((Send POST $callbackPath $null $lateSuccess (Sign-Callback $lateSuccess)).Data.status -eq 'FAILED' -and
        (Send GET "$end/me" $employer.token).Data.balance -eq 100000) 'SUCCESS đến sau FAILED không cộng tiền'

    $simultaneous = (Send POST "$end/top-up" $employer.token @{amount = 50000; idempotencyKey = "$key-parallel"}).Data
    $signedCallback = New-Callback $simultaneous.id 50000 "$key-parallel"
    $parallel = @(Send-Parallel POST $callbackPath $null $signedCallback (Sign-Callback $signedCallback))
    Check ($parallel[0].Status -eq 200 -and $parallel[1].Status -eq 200 -and
        $parallel[0].Data.completedAt -eq $parallel[1].Data.completedAt -and
        (Send GET "$end/me" $employer.token).Data.balance -eq 150000) 'Hai callback đồng thời cộng tiền đúng một lần'

    $history = Send GET "$end/transactions?pageSize=1" $employer.token
    Check ($history.Data.totalCount -eq 2 -and $history.Data.items.Count -eq 1 -and
        $history.Data.items[0].refId -eq $simultaneous.id) 'Lịch sử phân trang chỉ gồm bút toán hoàn tất'
    Check ((Send GET "$end/transactions?walletId=$walletId" $other.token).Data.totalCount -eq 0) 'Lịch sử ví khác không lộ giao dịch'
    $firstId = (Send POST "$end/top-up" $other.token @{amount = 50000; idempotencyKey = "$key-distinct-1"}).Data.id
    $secondId = (Send POST "$end/top-up" $other.token @{amount = 75000; idempotencyKey = "$key-distinct-2"}).Data.id
    $firstCallback = New-Callback $firstId 50000 "$key-distinct-1"
    $secondCallback = New-Callback $secondId 75000 "$key-distinct-2"
    $distinct = @(Send-Parallel POST $callbackPath $null $firstCallback (Sign-Callback $firstCallback) $secondCallback (Sign-Callback $secondCallback))
    Check ($distinct[0].Status -eq 200 -and $distinct[1].Status -eq 200 -and
        (Send GET "$end/me" $other.token).Data.balance -eq 125000) 'Hai callback khác mã trên cùng ví không mất cập nhật'
    $meta = Sql "SELECT COUNT(*) FROM dbo.OutboxMessages WHERE EventType = 'WalletTopUpCompleted' AND JSON_VALUE(Payload, '$.TopUpId') = '$id' AND JSON_VALUE(Payload, '$.SchemaVersion') = '1' AND TRY_CONVERT(uniqueidentifier, JSON_VALUE(Payload, '$.EventId')) = Id AND ISNULL(JSON_VALUE(Payload, '$.CorrelationId'), '') <> '';"
    Check ($meta -eq '1') 'Outbox duy nhất, đủ metadata BR-18'
    $txnId = $history.Data.items[0].id
    $guard = Sql @"
BEGIN TRY
    BEGIN TRANSACTION;
    UPDATE dbo.Transactions SET Amount = 1 WHERE Id = '$txnId';
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER();
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    DELETE FROM dbo.Transactions WHERE Id = '$txnId';
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER();
END CATCH;
IF @@TRANCOUNT > 0 ROLLBACK;
"@
    Check ((($guard -split '\r?\n') -join ',') -eq '51000,51000') 'SQL chặn sửa và xóa sổ giao dịch'

    $rollbackId = (Send POST "$end/top-up" $employer.token @{amount = 50000; idempotencyKey = "$key-rollback"}).Data.id
    $rollbackCallback = New-Callback $rollbackId 50000 "$key-rollback"
    $rollbackSignature = Sign-Callback $rollbackCallback
    $trigger = "TR_TestWallet_$([guid]::NewGuid().ToString('N'))"
    try {
        Sql @"
CREATE TRIGGER dbo.[$trigger] ON dbo.OutboxMessages AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE JSON_VALUE(Payload, '$.TopUpId') = '$rollbackId')
        THROW 51099, 'Test Wallet Outbox rollback.', 1;
END;
"@ | Out-Null
        Status (Send POST $callbackPath $null $rollbackCallback $rollbackSignature) 500 'Mô phỏng lỗi ghi Outbox'
        $state = Sql "SELECT CONCAT(p.Status, '|', w.Balance, '|', (SELECT COUNT(*) FROM dbo.Transactions WHERE RefId = p.Id), '|', (SELECT COUNT(*) FROM dbo.OutboxMessages WHERE JSON_VALUE(Payload, '$.TopUpId') = '$rollbackId')) FROM dbo.TopUpPayments p JOIN dbo.Wallets w ON w.Id = p.WalletId WHERE p.Id = '$rollbackId';"
        Check ($state -eq 'Pending|150000|0|0') 'Rollback yêu cầu, số dư, bút toán và Outbox cùng nhau'
    }
    finally { Sql "DROP TRIGGER IF EXISTS dbo.[$trigger];" | Out-Null }
    Status (Send POST $callbackPath $null $rollbackCallback $rollbackSignature) 200 'Callback retry thành công sau khi SQL phục hồi'

    try {
        Sql "UPDATE dbo.Wallets SET Balance = 200001 WHERE Id = '$walletId';" | Out-Null
        $found = $false
        for ($i = 0; $i -lt 10; $i++) {
            $log = Compose logs --since 2m --no-color payment-api
            $found = [bool]($log | Where-Object { $_.Contains($walletId) -and $_.Contains('số dư 200001') } | Select-Object -First 1)
            if ($found) { break }
            Start-Sleep -Seconds 1
        }
        Check $found 'Đối soát ghi cảnh báo sai lệch'
        Check ((Sql "SELECT Balance FROM dbo.Wallets WHERE Id = '$walletId';") -eq '200001') 'Đối soát giữ nguyên số dư'
    }
    finally { Sql "UPDATE dbo.Wallets SET Balance = 200000 WHERE Id = '$walletId';" | Out-Null }

    Status (Send GET 'api/v1/jobs?page=1' $other.token) 200 'Regression tìm việc qua Gateway'
    $notifications = Send GET 'api/v1/notifications' $other.token
    Check ($notifications.Status -eq 200 -and $notifications.Data.eventType -contains 'UserRegistered') 'Regression Notification lưu thông báo OTP'

    $before = Sql "SELECT COUNT(*) FROM dbo.TopUpPayments WHERE WalletId = '$walletId';"
    Compose pause identity-api | Out-Null
    try {
        $unavailable = Send POST "$end/top-up" $employer.token @{amount = 50000; idempotencyKey = "$key-identity"}
        Check ($unavailable.Status -eq 503 -and $unavailable.RetryAfter.Delta.TotalSeconds -eq 10) 'Identity lỗi trả 503 kèm Retry-After'
        Check ((Sql "SELECT COUNT(*) FROM dbo.TopUpPayments WHERE WalletId = '$walletId';") -eq $before) 'Identity lỗi không tạo yêu cầu nạp dở'
    }
    finally { Compose unpause identity-api | Out-Null }

    $recovered = $null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        $recovered = Send GET "$end/me" $employer.token
        if ($recovered.Status -eq 200) { break }
        Start-Sleep -Seconds 2
    }
    Status $recovered 200 'Ví phục hồi sau khi Identity sẵn sàng'

    $guardPayment = (Send POST "$end/top-up" $employer.token @{amount=50000; idempotencyKey="$key-production"}).Data
    $guardCallback = New-Callback $guardPayment.id 50000 "$key-production"
    $savedEnvironment = $env:PAYMENT_ENVIRONMENT
    $savedGateway = $GatewayUrl
    try {
        $env:PAYMENT_ENVIRONMENT = 'Production'
        Compose up -d --no-deps --wait payment-api | Out-Null
        $GatewayUrl = $DirectPaymentUrl
        $me = Send GET "$end/me" $employer.token
        Check ($me.Status -eq 200 -and !$me.Data.canTopUp) 'Production vẫn đọc ví nhưng cờ mô phỏng tắt'
        Status (Send POST "$end/top-up" $employer.token @{amount=50000; idempotencyKey="$key-blocked"}) 503 'Production chặn khởi tạo mô phỏng'
        Status (Send POST "$end/mock-payments/$($guardPayment.id)/complete" $employer.token @{success=$true}) 404 'Production chặn cổng mô phỏng'
        Status (Send POST $callbackPath $null $guardCallback (Sign-Callback $guardCallback)) 404 'Production chặn cả callback có chữ ký thử nghiệm'
        Check ((Send GET "$end/top-up/$($guardPayment.id)" $employer.token).Data.status -eq 'Pending' -and
            (Send GET "$end/me" $employer.token).Data.balance -eq 200000) 'Production không ghi tiền từ mô phỏng'
    }
    finally {
        $GatewayUrl = $savedGateway
        $env:PAYMENT_ENVIRONMENT = $savedEnvironment
        Compose up -d --no-deps --wait payment-api | Out-Null
    }

    $outboxQuery = "SELECT CASE WHEN PublishedAt IS NULL THEN 'Pending' ELSE 'Published' END FROM dbo.OutboxMessages WHERE JSON_VALUE(Payload, '$.TopUpId') = '$($guardPayment.id)';"
    try {
        $GatewayUrl = $DirectPaymentUrl
        Compose stop rabbitmq | Out-Null
        $settledWithoutBroker = Send POST $callbackPath $null $guardCallback (Sign-Callback $guardCallback)
        Check ($settledWithoutBroker.Status -eq 200 -and $settledWithoutBroker.Data.status -eq 'SUCCESS' -and
            (Send GET "$end/me" $employer.token).Data.balance -eq 250000) 'RabbitMQ tắt vẫn commit nạp tiền'
        Check ((Sql $outboxQuery) -eq 'Pending') 'Outbox giữ sự kiện chưa phát khi RabbitMQ tắt'
        $health = $Http.GetStringAsync("$DirectPaymentUrl/health").GetAwaiter().GetResult()
        Check ($health -eq 'Degraded') 'Health báo Degraded khi RabbitMQ tắt'
    }
    finally {
        $GatewayUrl = $savedGateway
        Compose up -d --no-deps --wait rabbitmq | Out-Null
    }
    $delivery = $null
    for ($i = 0; $i -lt 15; $i++) {
        $delivery = Sql $outboxQuery
        if ($delivery -eq 'Published') { break }
        Start-Sleep -Seconds 1
    }
    Check ($delivery -eq 'Published') 'Outbox phát lại sau khi RabbitMQ phục hồi'

    Write-Host "Integration: $script:checks PASS."
}
finally { $Http.Dispose() }
