(function () {
    'use strict';
    if (window.gelatoHomeRows) return;
    window.gelatoHomeRows = true;
    let busy = false;
    let lastUser = null;
    let nextRefresh = 0;
    const read = (o, k) => o[k] ?? o[k[0].toLowerCase() + k.slice(1)];
    const path = row => 'gelato/catalogs/home/' + ['Source', 'Type', 'Id'].map(k => encodeURIComponent(read(row, k))).join('/');
    async function refresh() {
        const api = window.ApiClient;
        const user = api?.getCurrentUserId();
        const home = document.querySelector('#indexPage:not(.hide) .homeSectionsContainer');
        if (user !== lastUser) {
            document.querySelectorAll('.gelato-home').forEach(el => el.remove());
            lastUser = user; nextRefresh = 0;
        }
        if (!home || !user || document.hidden || busy) return;
        if (Date.now() < nextRefresh && home.querySelector('.gelato-home')) return;
        busy = true;
        try {
            const rows = await api.getJSON(api.getUrl('gelato/catalogs/home'));
            if (api.getCurrentUserId() !== user || !home.isConnected) return;
            const keys = new Set();
            // Limit concurrent row requests without blocking Jellyfin's own home requests.
            for (let offset = 0; offset < rows.length; offset += 3) {
                await Promise.all(rows.slice(offset, offset + 3).map(async row => {
                    const key = path(row); keys.add(key);
                    let section = [...home.querySelectorAll('.gelato-home')].find(el => el.dataset.key === key);
                    try {
                        const items = await api.getJSON(api.getUrl(key));
                        if (api.getCurrentUserId() !== user || !home.isConnected) return;
                        const signature = JSON.stringify([read(row, 'Name'), items]);
                        if (section?.dataset.signature === signature) return;
                        const replacement = document.createElement('section');
                        replacement.className = 'verticalSection gelato-home';
                        replacement.dataset.key = key; replacement.dataset.signature = signature;
                        const title = document.createElement('h2'); title.className = 'sectionTitle';
                        title.textContent = read(row, 'Name'); replacement.appendChild(title);
                        const cards = document.createElement('div');
                        cards.style.cssText = 'display:flex;gap:1em;overflow-x:auto;padding:0.5em 0 1em';
                        items.forEach(item => {
                            const card = document.createElement('button'); card.type = 'button';
                            card.className = 'emby-button'; card.style.cssText = 'flex:0 0 145px;white-space:normal;text-align:left';
                            const poster = read(item, 'Poster');
                            if (poster) {
                                const image = document.createElement('img'); image.src = poster;
                                image.loading = 'lazy'; image.decoding = 'async'; image.alt = '';
                                image.width = 145; image.height = 218; image.style.objectFit = 'cover';
                                card.appendChild(image);
                            }
                            const label = document.createElement('div'); label.textContent = read(item, 'Name'); card.appendChild(label);
                            card.onclick = async () => {
                                card.disabled = true;
                                try {
                                    const result = await api.ajax({ type: 'POST', url: api.getUrl(key + '/open/' + encodeURIComponent(read(item, 'Id'))), dataType: 'json' });
                                    if (api.getCurrentUserId() === user) location.hash = '#/details?id=' + encodeURIComponent(read(result, 'Id')) + '&serverId=' + encodeURIComponent(api.serverId());
                                } catch (_) { label.textContent = 'Unable to open. Select to retry.'; }
                                finally { card.disabled = false; }
                            };
                            cards.appendChild(card);
                        });
                        replacement.appendChild(cards);
                        if (section) section.replaceWith(replacement); else home.appendChild(replacement);
                    } catch (_) { /* Keep previous row during a provider outage. */ }
                }));
            }
            home.querySelectorAll('.gelato-home').forEach(el => { if (!keys.has(el.dataset.key)) el.remove(); });
            nextRefresh = Date.now() + 30000;
        } catch (_) { nextRefresh = Date.now() + 30000; }
        finally { busy = false; }
    }
    document.addEventListener('viewshow', refresh);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('hashchange', () => { nextRefresh = 0; refresh(); });
    setInterval(refresh, 30000);
    refresh();
}());
