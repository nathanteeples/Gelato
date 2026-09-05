const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const dom = new JSDOM('<div id="indexPage"><div class="homeSectionsContainer"></div></div>', { url: 'https://jellyfin.test/web/', runScripts: 'outside-only', pretendToBeVisual: true });
const { window } = dom;
let user = 'alice'; let title = 'Because you watched A'; let rows = true; let fail = false; let timer;
window.setInterval = callback => { timer = callback; };
window.ApiClient = {
    getCurrentUserId: () => user,
    getUrl: p => p,
    getJSON: async p => {
        if (p === 'gelato/catalogs/home') return rows ? [{ Source: 'source', Type: 'movie', Id: 'slot', Name: title }] : [];
        if (fail) throw new Error('outage');
        return [{ Id: 'tt1', Name: '<img src=x onerror=alert(1)>', Poster: 'https://image.tmdb.org/t/p/w342/a.jpg' }];
    }
};
const source = fs.readFileSync(path.join(__dirname, '../Frontend/js/catalog-home.js'), 'utf8');
const flush = () => new Promise(resolve => setTimeout(resolve, 15));
(async () => {
    window.eval(source); await flush();
    assert.equal(window.document.querySelectorAll('.gelato-home').length, 1);
    assert.equal(window.document.querySelectorAll('img').length, 1, 'untrusted title is rendered as text');
    assert.equal(window.document.querySelector('img').loading, 'lazy');
    const section = window.document.querySelector('.gelato-home');
    window.dispatchEvent(new window.HashChangeEvent('hashchange')); await flush();
    assert.equal(window.document.querySelector('.gelato-home'), section, 'unchanged row preserves DOM');
    title = 'Because you watched B';
    window.dispatchEvent(new window.HashChangeEvent('hashchange')); await flush();
    assert.equal(window.document.querySelector('h2').textContent, title, 'dynamic title updates');
    fail = true;
    window.dispatchEvent(new window.HashChangeEvent('hashchange')); await flush();
    assert.equal(window.document.querySelectorAll('.gelato-home').length, 1, 'outage preserves prior row');
    user = null; timer(); await flush();
    assert.equal(window.document.querySelectorAll('.gelato-home').length, 0, 'logout removes personalized rows');
    user = 'bob'; fail = false; timer(); await flush();
    assert.equal(window.document.querySelectorAll('.gelato-home').length, 1);
    rows = false;
    window.dispatchEvent(new window.HashChangeEvent('hashchange')); await flush();
    assert.equal(window.document.querySelectorAll('.gelato-home').length, 0, 'removed rows disappear');
    console.log('9 browser behavior checks passed.');
    dom.window.close();
})().catch(error => { console.error(error); dom.window.close(); process.exitCode = 1; });
