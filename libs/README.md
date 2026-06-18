# libs/

Drop **`vxlapi_NET.dll`** here to build the optional `src/UdsOnCan.Vector` project.

It ships with the **Vector XL Driver Library** (typically under
`C:\Users\Public\Documents\Vector\XL Driver Library\bin\` or the SDK's `.NET`
folder). It is Vector's proprietary, licensed assembly, so it is **not** committed
to this repository (`*.dll` here is git-ignored).

```
libs/
  vxlapi_NET.dll   ← you provide this
```

The core library (`src/UdsOnCan`) and the demo do **not** need this DLL — they
build and run anywhere. Only `UdsOnCan.Vector` (the real-hardware CAN binding)
references it.
