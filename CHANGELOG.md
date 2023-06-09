# Changelog for PSDetour

## v0.3.0 - TBD

* Added `Trace-PSDetourProcess` to make it easier to start hooks for auditing in other processes
  * This provides a common mechanism that can be used to output data from a remote hook as well as wait for input data in the hook itself
* Provides a `DetouredModules` property in the hooks `$this` variable
  * This provides access to other detoured method's InvokeContext allowing the hook to call the underlying API
* Remove separate parameter sets for `New-PSDetourHook`
  * A breaking change is that `DllName` and `MethodName` must be specified with `Address` now
* Added option `-AddressIsOffset` to specify `-Address` is located at the offset of the `-DllName` when loaded in the process

## v0.2.0 - 2023-04-18

* Added `-Address` parameter to `New-PSDetourHook` to hook a method at a specific address offset rather than by name.

## v0.1.1 - 2022-11-29

* Remove `Console.WriteLine` call used for testing

## v0.1.0 - 2022-10-25

* Initial version of the `PSDetour` module
