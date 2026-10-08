# Requirements coverage

The project follows the supplied email's application goals, with the agreed client-only OGC boundary and advanced topics as extension lessons.

| Requirement | Delivered coverage |
|---|---|
| Architecture and rationale | Lesson 01, project map, pinned API reference |
| Complete solution, csproj, App/MainWindow files | Complete generated source listings and runnable projects |
| MVVM, DI, logging, appsettings | Lesson 03 and configuration reference |
| Layer tree and dynamic management | Lesson 06; visibility, opacity, order, zoom, add/remove |
| WMS | Lesson 07; 1.1.1/1.3.0, map images, conditional GetFeatureInfo |
| WMTS | Lesson 08; 1.0.0, explicit style/matrix-set selection |
| WFS | Lesson 09; 1.1/2.0, bounded GML 3.1/3.2 subset |
| GeoJSON and graphics | Lesson 05; local points, lines, polygons and attributes |
| Selection and highlighting | Independent cloned selection overlay |
| Authentication | Anonymous/Basic over HTTPS; all request stages |
| Coordinate transformations | 4326/3857 plus tested protocol axis-order rules |
| GeoPackage recommendations | Advanced extension design and acceptance cases |
| SQLite and GDAL integration | Advanced extension designs, boundaries, and GDAL command example |
| Offline cache, search, editing | Advanced extension designs; not core implementations |
| Deployment and production guidance | Lesson 11 and enterprise integration extension |
| Troubleshooting and performance | Lesson 10, bounds, debounce and failure tests |
| GeoServer best practices | Reframed as generic OGC client/provider contract; server administration excluded |
| PostGIS | Possible server-side data store; no client database access |
| Package reference list | Project files, API reference, per-project locks |
| Human and local-LLM documentation | Eleven numbered lessons, compact LLM context, combined source-inclusive manual |

The local fixture server is test infrastructure only. It is not a substitute GIS server or an additional deployment dependency. Actual work-service URLs, authentication, data, and Artifactory access have not been supplied to this workspace.
