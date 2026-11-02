# LiteNetLib (vendored)

- Upstream: https://github.com/RevenantX/LiteNetLib
- Version: **2.1.5** (git tag `2.1.5`, commit `aa6d03e548b8d23308905431be6f2273a5c0f363`)
- License: MIT (see `LICENSE.txt`)

The same source files are compiled into the Unity client (this folder) and into the
.NET dedicated server (`Server/src/Survival.Shared/Survival.Shared.csproj` includes this folder),
so both sides always speak exactly the same transport protocol.

Do not edit these files; to upgrade, replace the folder with a newer tag and bump
`NetProtocol.Version` in `Assets/Survival/Networking/Protocol/NetProtocol.cs`.
