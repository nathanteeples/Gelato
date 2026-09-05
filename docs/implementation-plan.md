# Stremio integration scope

Work from nathanteeples/Gelato, branch codex/stremio-catalogue-support.

## Architecture and build

Gelato is a .NET 9 server plugin targeting Jellyfin 10.11.6. Build with
`dotnet build -c Release`; the project embeds configuration and browser scripts.
Existing search creates virtual items; opening an item fetches complete metadata
and inserts it into the library. JavaScript Injector registers the browser assets.

## Implementation

1. Accept standard string/object manifest resources and capability restrictions.
   Preserve the existing URL setting, add additional addon URLs, and route streams
   concurrently with failure isolation. Use a separately configured AIOMetadata
   manifest/base URL for metadata and search, bypassing the stream aggregator.
2. Bound metadata caches, coalesce identical requests, refresh manifests, dispose
   HTTP responses, and preserve case-sensitive endpoint identities. Never log
   configured URL paths because they can contain credentials.
3. Discover catalogues using endpoint + type + id identity. Preserve administrator
   settings without writing configuration on reads. Preview rows use catalogue
   metadata only, bounded cached snapshots, and periodic refresh while viewed.
   Failed refreshes retain the previous snapshot. No full library scan per row.
4. Add authenticated preview endpoints and an opt-in Jellyfin Web home extension.
   Cards lazy-load small artwork and resolve full details only when opened.
5. Correct import defaults, respect pagination capabilities, and apply collection
   membership differences instead of rebuilding all membership.

## Compatibility boundaries

Protocol support does not guarantee every addon can be played by Jellyfin.
Movie/series resources and Jellyfin-compatible streams are the initial target;
custom media types, browser-only external playback, DRM and interactive addon
features need separate client support. Native Jellyfin clients cannot render
injected Web scripts. Watchly/Experience-specific acceptance testing requires
identifying their implementations and a configured test server.

## Verification

Build the plugin; exercise protocol parsing, routing, request coalescing, cache
expiry/failure behavior, endpoint isolation and catalogue refresh with fake HTTP
responses. Check browser script syntax. Live Jellyfin acceptance: authenticate as
two differently configured users, load home, revisit cached rows, change an addon
manifest, simulate provider failure, open a movie/series, then play a stream.

Protocol reference: https://github.com/Stremio/stremio-addon-sdk/blob/master/docs/protocol.md
Manifest reference: https://github.com/Stremio/stremio-addon-sdk/blob/master/docs/api/responses/manifest.md

## Implemented and remaining

Server protocol routing, direct metadata, cached previews, Web extension, import delta
updates and native thumbnail selection are implemented with automated checks. The user
requested **all native clients**: their direct home-row UI remains separate client work,
not completed by the Web extension. See setup-and-client-support.md for the API contract
and explicit acceptance gaps. Experience has not been identified; Watchly source review
is complete. No live-server performance or playback results are claimed.
