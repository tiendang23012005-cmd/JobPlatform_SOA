const { spawn, execFileSync } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const { mkdtempSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join } = require('node:path');

const base = process.env.WALLET_TEST_CLIENT_URL || 'http://localhost:5510/';
const gateway = 'http://localhost:5500';
const chrome = process.env.WALLET_TEST_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const profile = mkdtempSync(join(tmpdir(), 'jobplatform-wallet-chrome-'));
const compose = ['compose', '-p', 'jobplatform-wallet-test', '-f', 'docker-compose.yml', '-f', 'tests/docker-compose.wallet-test.yml'];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const pending = new Map();
let browser, socket, sequence = 0, droppedId, dropTopUpResponse = false;
const browserErrors = [];

async function until(predicate, description, seconds = 35) {
    const limit = Date.now() + seconds * 1000;
    while (Date.now() < limit) {
        try { if (await predicate()) return; }
        catch (error) { if (error.message !== 'fetch failed') browserErrors.push(error.message); }
        await delay(150);
    }
    const page = socket?.readyState === 1 ? await JS('document.body?.innerText?.slice(0, 550)').catch(() => '') : '';
    throw new Error([description, page, ...browserErrors.slice(-3)].join('\n'));
}

function CDP(method, params = {}) {
    const id = ++sequence;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        socket.send(JSON.stringify({ id, method, params }));
    });
}

async function JS(expression) {
    const response = await CDP('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description || response.exceptionDetails.text);
    return response.result?.value;
}

const see = phrase => until(() => JS('document.body?.innerText?.includes(' + JSON.stringify(phrase) + ')'), phrase);
const goto = path => CDP('Page.navigate', { url: new URL(path, base).href });
const clickText = value => JS('Array.from(document.querySelectorAll("button,a")).find(e => e.innerText.trim() === ' + JSON.stringify(value) + ').click()');
const fill = (selector, value) => JS('(() => { const e = document.querySelector(' +
    JSON.stringify(selector) + '); e.value = ' + JSON.stringify(value) +
    '; e.dispatchEvent(new Event("change", { bubbles: true })); })()');

async function bindBrowser() {
    let target;
    await until(async () => {
        const targets = await (await fetch('http://127.0.0.1:9228/json/list')).json();
        target = targets.find(item => item.type === 'page');
        return target;
    }, 'Chrome kiểm thử chưa sẵn sàng', 15);

    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => {
        socket.addEventListener('open', resolve, { once: true });
        socket.addEventListener('error', reject, { once: true });
    });
    socket.addEventListener('message', ({ data }) => {
        const message = JSON.parse(data);
        if (message.id && pending.has(message.id)) {
            const call = pending.get(message.id);
            pending.delete(message.id);
            if (message.error) call.reject(new Error(message.error.message));
            else call.resolve(message.result);
        } else if (message.method === 'Fetch.requestPaused') {
            handleRequest(message.params).catch(error => browserErrors.push(error.message));
        } else if (message.method === 'Runtime.exceptionThrown') {
            browserErrors.push(message.params.exceptionDetails.exception?.description || message.params.exceptionDetails.text);
        }
    });

    await CDP('Page.enable');
    await CDP('Runtime.enable');
    await CDP('Fetch.enable', { patterns: [
        { urlPattern: new URL('appsettings.json', base).href },
        { urlPattern: gateway + '/api/v1/wallet/top-up', requestStage: 'Response' }
    ] });
    await CDP('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
}

async function handleRequest(event) {
    const requestId = event.requestId;
    if (event.request.url === gateway + '/api/v1/wallet/top-up') {
        if (!dropTopUpResponse || event.request.method !== 'POST' || event.responseStatusCode !== 200) {
            await CDP('Fetch.continueResponse', { requestId });
            return;
        }
        dropTopUpResponse = false;
        const payload = await CDP('Fetch.getResponseBody', { requestId });
        const text = payload.base64Encoded ? Buffer.from(payload.body, 'base64').toString('utf8') : payload.body;
        droppedId = JSON.parse(text).id;
        await CDP('Fetch.failRequest', { requestId, errorReason: 'Failed' });
        return;
    }

    await CDP('Fetch.fulfillRequest', {
        requestId, responseCode: 200,
        responseHeaders: [{ name: 'content-type', value: 'application/json' }],
        body: Buffer.from(JSON.stringify({ GatewayUrl: gateway })).toString('base64')
    });
}

async function fixture() {
    const nonce = randomUUID();
    const email = 'wallet-browser-' + nonce + '@example.test';
    const password = 'Wallet@9' + nonce;
    const response = await fetch(gateway + '/api/v1/auth/register', {
        method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email, password, fullName: 'Kiểm thử giao diện ví', role: 'Employer' })
    });
    if (response.status !== 201) throw new Error('Đăng ký tài khoản thử nghiệm thất bại.');
    let otp;
    await until(() => {
        const logs = execFileSync('docker', [...compose, 'logs', '--since', '2m', '--no-color', 'notification-api'], {
            encoding: 'utf8', windowsHide: true
        });
        const line = logs.split(/\r?\n/).find(value => value.includes('[EMAIL-DEV]') && value.includes(email));
        otp = line?.match(/\b\d{6}\b/)?.[0];
        return otp;
    }, 'Chưa nhận được OTP thử nghiệm.', 25);
    return { email, password, otp };
}

async function startTopUp(amount, drop = false) {
    await fill('#wallet-amount', String(amount));
    await clickText('Tiếp tục');
    await see('Xác nhận nạp');
    dropTopUpResponse = drop;
    await clickText('Xác nhận và tiếp tục');
}

async function run() {
    const user = await fixture();
    browser = spawn(chrome, [
        '--headless=new', '--incognito', '--disable-gpu', '--disable-background-networking',
        '--no-first-run', '--remote-debugging-port=9228', '--user-data-dir=' + profile, 'about:blank'
    ], { windowsHide: true, stdio: 'ignore' });
    browser.on('error', error => browserErrors.push(error.message));
    await bindBrowser();

    await goto('verify?email=' + encodeURIComponent(user.email));
    await see('Xác minh email');
    await fill('input.jp-otp', user.otp);
    await clickText('Xác minh');
    await see('Xác minh email thành công');
    await fill('input[placeholder="ban@example.com"]', user.email);
    await fill('input[type="password"]', user.password);
    await clickText('Đăng nhập');
    await until(() => JS('!!document.querySelector(".jp-sidebar")'), 'Đăng nhập và sidebar');
    console.log('PASS OTP, đăng nhập và sidebar của nhóm');

    await goto('soon/vi');
    await see('Chưa có giao dịch nào.');
    await see('Số dư đang khóa');
    await fill('#wallet-amount', '10000');
    if (!await JS('document.querySelector("#wallet-amount").validity.rangeUnderflow'))
        throw new Error('Giao diện thiếu ngưỡng 50.000 VNĐ.');
    console.log('PASS Ví mới và validation 50.000 VNĐ');

    await startTopUp(100000, true);
    await see('Thử lại với mã đã lưu');
    if (!droppedId) throw new Error('Chưa mô phỏng được mất phản hồi sau khi lưu Pending.');
    await goto('soon/vi');
    await see('Thử lại với mã đã lưu');
    await clickText('Thử lại với mã đã lưu');
    await see('Cổng thanh toán mô phỏng');
    await see('Đang chờ thanh toán');
    if (await JS('location.pathname.split("/").pop()') !== droppedId) throw new Error('Retry tạo yêu cầu mới.');
    console.log('PASS Tải lại sau khi mất phản hồi, retry nhận đúng yêu cầu Pending');

    await clickText('Xác nhận thanh toán');
    await see('Đã nạp 100.000 ₫ vào ví.');
    await clickText('Về ví điện tử');
    await until(() => JS('document.querySelectorAll("tbody tr").length === 1'), 'Hiển thị giao dịch đã ghi sổ');
    if ((await JS('document.querySelector(".card .text-primary").textContent')).trim() !== '100.000 ₫')
        throw new Error('Số dư khả dụng hiển thị sai.');
    if (await JS('document.documentElement.scrollWidth > innerWidth + 1'))
        throw new Error('Trang ví tràn chiều rộng trên màn hình 390 px.');
    console.log('PASS Callback thành công, số dư và lịch sử đúng trên màn hình 390 px');

    await clickText('Nạp thêm');
    await startTopUp(50000);
    await see('Cổng thanh toán mô phỏng');
    await clickText('Hủy thanh toán');
    await see('Thanh toán thất bại hoặc đã hủy.');
    await clickText('Về ví điện tử');
    await see('Thất bại');
    if (await JS('document.querySelectorAll("tbody tr").length') !== 1) throw new Error('Hủy giao dịch tạo thêm bút toán.');
    console.log('PASS Hủy thanh toán hiển thị FAILED và giữ nguyên lịch sử');

    await goto('notifications');
    await see('Mã xác minh đã được gửi tới');
    await goto('tin-cua-toi');
    await until(() => JS('document.body?.innerText?.includes("Tin") && !!document.querySelector(".jp-sidebar")'), 'Trang Job của Employer');
    console.log('PASS Notification và trang Job còn điều hướng được qua layout hiện tại');
}

run().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
}).finally(() => {
    socket?.close();
    browser?.kill();
});
