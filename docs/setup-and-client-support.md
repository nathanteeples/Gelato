# Setup and client support

This branch extends the existing fork; it does not create a new fork or deploy to a server.

## Configure addons

1. Build with .NET 9: `dotnet build -c Release`. Install the output plugin and its
   bundled dependencies using your existing Gelato installation procedure, then restart Jellyfin.
2. In Gelato settings, enter a primary configured Stremio manifest URL. Add other
   stream, catalogue or subtitle addons in **Additional Stremio addons**, one per line.
3. Enter your configured **AIOMetadata endpoint** directly. Detail metadata and search
   use this endpoint without contacting the stream aggregator. Leave it blank to
   discover metadata/search resources among your configured addons.
4. Configure movie/series library paths as before. Existing per-user overrides remain
   supported and now have metadata and additional-addon fields. Additional global
   addon URLs are intentionally not inherited by a user override; explicitly configure
   each user's personalized catalogue endpoints.
5. Save settings before refreshing the Catalogs tab. Select catalogues for import and
   optional collections. Import limits of zero use the global limit. Imports have a
   default 30-minute scheduled trigger (existing installations may retain their old
   task schedule; configure it in Jellyfin Scheduled Tasks).
6. For Web previews, enable **catalogue preview API and Web home rows** and **frontend
   customization**, with Jellyfin JavaScript Injector installed. All discovered
   movie/series catalogues without required filters appear by default; use Home row
   checkboxes to hide them. Import is independent of home-preview visibility.

Manifest/preview refresh defaults to 300 seconds. Provider request timeout defaults
 to 10 seconds (configurable from 2–120). Stream requests are concurrent; an unresponsive
addon can delay completion until its timeout. Cached metadata avoids repeated detail
requests for five minutes. Existing Jellyfin stream TTL behavior remains in place.

## Native clients

The shared server implementation supports imported movies, series and Jellyfin
collections. Client presentation and codec support still vary.

| Surface | Implemented here | Remaining work |
| --- | --- | --- |
| Jellyfin Web + JavaScript Injector | Dynamic home rows with lazy preview cards | Live-server acceptance testing |
| Web-based wrappers | Same script can work if the wrapper loads injected Jellyfin Web | Verify each wrapper |
| Native Android TV, Swiftfin and other native clients | Server-side catalogue imports and collections; authenticated preview API | Client UI integration for direct home rows and device testing |

**This branch does not implement home rows in every native Jellyfin client.** A server
plugin cannot add arbitrary UI to those applications. Jellyfin tracks this separately
in [Collections as home sections](https://github.com/jellyfin/jellyfin-meta/discussions/83).
To implement it in a native client: fetch the row list, lazily request visible row
previews, periodically refresh while home is visible, preserve stale rows during an
outage, and call the open endpoint before navigating to the ordinary Jellyfin details
screen. Use authenticated requests and clear personalized rows when changing users.
Do not send a user ID query parameter to select someone else's rows.

### Preview API

- `GET /gelato/catalogs/home` returns ordered `{Source, Id, Type, Name}` rows.
- `GET /gelato/catalogs/home/{source}/{type}/{id}` returns up to 40 lightweight
  `{Id, Type, Name, Poster, Year}` previews. Source is an opaque identifier, not a URL.
- `POST /gelato/catalogs/home/{source}/{type}/{id}/open/{itemId}` returns a virtual
  Jellyfin `{Id}`. The existing details interception hydrates the selected item.
- JSON casing follows Jellyfin serialization settings; integrations should use the
  deployed server's response casing. URL-encode each path segment.
- All preview endpoints use the authenticated user's configuration. Catalogue
  administration and import endpoints require administrator elevation.

## Performance and compatibility

Previews never import items, expand series trees or fetch detail metadata. Cache refresh
is coalesced, bounded and serves stale results during refresh/outages; failures back off
for 30 seconds. Watchly reuses slot IDs with changing names: a renamed row gets a new
preview snapshot. Reading manifests never overwrites saved catalogue settings.

Lazy images default on for new configurations. Small TMDB poster requests in both Web
previews and the native image processing path use `w342`, without replacing the original
image reference. Arbitrary poster-provider URLs remain opaque: resizing is only safe
where the provider exposes a known size API. Existing images already downloaded by
Jellyfin are processed by Jellyfin as usual.

Collection imports are serialized, stop when pagination is unsupported, deduplicate
pages, update names, and apply membership differences. Partial item failures preserve
prior membership; row refreshes do not start a full library scan. Collections use
source/type/id identity; primary-source legacy collections are migrated when refreshed.
Imported catalogue items remain in the library when removed from a collection, preserving
watch history and avoiding destructive library cleanup. Native collection display order
is controlled by the client/Jellyfin; arbitrary Stremio rank order is not guaranteed.

Standard resource strings/objects, type restrictions, ID-prefix restrictions and multiple
movie/series addons are supported. This is not a promise of compatibility with every
Stremio addon: custom media types, required-filter catalogue browsing, browser-only
external playback, DRM, and addon-specific proxy-header playback still need separate
work. Addons must supply IDs understood by the configured metadata and stream providers;
Gelato does not provide a universal ID conversion service.

## Watchly and outstanding validation

[Watchly](https://github.com/TimilsinaBimal/Watchly) serves changing manifest row names
and token-scoped recommendation catalogues. Its catalogue route ignores pagination
extras, so Gelato does not assume that every catalogue supports `skip`. The configured
Watchly token URL belongs in the user's addon list. Gelato does not sync watch history
to Watchly; use Watchly's supported Stremio/Trakt/Simkl history integration.

Experience-specific validation awaits its exact repository/manifest specification.
No live Jellyfin server or configured addon credentials were supplied. Remaining checks:
actual movie and series playback, two-user permission behavior, image processing with
real artwork, dynamic Watchly row changes, collections across native devices, and
client-specific home UI implementations.

## Verification

- `dotnet run --project Tests/Gelato.ProtocolTests.csproj -c Release`: 32 protocol,
  caching and native-thumbnail checks using controlled HTTP responses.
- `npm install --prefix Tests` then `npm test --prefix Tests`: 9 browser behavior checks.
- `dotnet build -c Release`: plugin build. Existing nullable/unread-parameter warnings
  remain; there are no build errors.

The tests prove request counts and routing behavior, not measured live-provider latency.
