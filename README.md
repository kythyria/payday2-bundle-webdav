This is a webdav server that exposes the contents of Payday 2 .bundle files. Requires .net 5.

Why do this? Because it's the least painful way to do a userspace filesystem on Windows. That makes
it so you don't have to actually *extract* the bundles to disk anywhere. 

## Command-line options
`--bundles=<path>` Set the directory containing `bundle_db.blb` (default is to autodetect).
`--urls=<url>` Set what URL prefix the server listens on. Default is `http://localhost:5000/`

## Routes
### `/diesel-raw`
WebDAV share containing all the files in the bundles. Which packages contain a file is exposed in a
property `<in-packages xmlns="https://ns.berigora.net/2020/payday2-tools"/>`.

### `/diesel-files`
Like the above, but some files are renamed and/or have their format converted:
|PD2 extension|New extension |Format change                            |
|-------------|--------------|-----------------------------------------|
|`texture`    |`dds`         |                                         |
|`movie`      |`bik`         |                                         |
|`strings`    |`strings.json`|JSON object mapping string IDs to strings|

## Mounting
Either of the above is a valid webdav collection, so a command like
```
net use P: \\localhost@5000\diesel-files\
```
will mount it, with `net use P: /delete` unmounting it. Or you can use the "Map Network Drive"
command, which can accept normal URLs directly.