$ErrorActionPreference='Stop'
try {$e=[Threading.EventWaitHandle]::OpenExisting('Local\EjectFixStop-6f44e2e3');$e.Set() | Out-Null;$e.Dispose();'Stop requested'} catch [Threading.WaitHandleCannotBeOpenedException] {'Worker is not running'}
