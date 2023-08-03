# Changelog for PSDetour

## v0.4.1 - TBD

* Allow using a scriptblock backed by a `FunctionDefinitionAst` (`${Function:Func-Name}`) for hooks provided to `Trace-PSDetourProcess`

## v0.4.0 - 2023-08-03

* Automatically define `-FunctionsToDefine` in the hooks being run with `Trace-PSDetourProcess`, no need to call `$this.State.GetFunction('Name')` to redefine it
  * `$this.State.GetFunction` has been removed and will no longer work
* Ensure the scriptblocks used with `Trace-PSDetourProcess` keep the original stacktrace locations for better debugging

## v0.3.1 - 2023-07-25

* Added lock for `Trace-PSDetourProcess` output pipe to avoid multiple threads clobbering the serialized output

## v0.3.0 - 2023-06-13

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
