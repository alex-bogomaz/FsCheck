(*** hide ***)
#I @"../../src/FsCheck/bin/Release"
#r @"../../packages/xunit.1.9.2/lib/net20/xunit.dll"
#r @"FsCheck"
open FsCheck
open System

(**
# Usage tips
    
## Properties of functions

Since FsCheck can generate random function values, it can check properties of 
functions. For example, we can check associativity of function composition as follows:*)

(***define-output:associativity***)
let associativity (x:int) (f:int->float,g:float->char,h:char->int) = ((f >> g) >> h) x = (f >> (g >> h)) x
Check.Quick associativity

(***include-output:associativity***)

(**
We can generate functions Tree -> _anything_. If a counter-example is found, function values will be displayed as <func>.

However, FsCheck can show you the generated function in more detail, with the Function type. 
If you use that, FsCheck can even shrink your function. For example:*)

(***define-output:mapRec***)
let mapRec (F (_,f)) (l:list<int>) =
  not l.IsEmpty ==>
      lazy (List.map f l = ((*f <|*) List.head l) :: List.map f (List.tail l))
Check.Quick mapRec

(***include-output:mapRec***)

(**
The type `Function<'a,'b>` records a map of all the arguments it was called with, and the result it produced. 
In your properties, you can extract the actual function by pattern matching as in the example. 
Function is used to print the function, and also to shrink it.
    
## Use pattern matching instead of forAll to use custom generators*)

(***define-output:EvenInt***)
type EvenInt = EvenInt of int with
  static member op_Explicit(EvenInt i) = i

type ArbitraryModifiers =
    static member EvenInt() = 
        Arb.from<int> 
        |> Arb.filter (fun i -> i % 2 = 0) 
        |> Arb.convert EvenInt int
        
Arb.register<ArbitraryModifiers>()

let ``generated even ints should be even`` (EvenInt i) = i % 2 = 0
Check.Quick ``generated even ints should be even``

(***include-output:EvenInt***)

(**
This make properties much more readable, especially since you can define custom shrink functions as well.

FsCheck has quite a few of these, e.g. `NonNegativeInt`, `PositiveInt`, `StringWithoutNullChars` etc. Have a look at the 
default Arbitrary instances on the `Arb.Default` type.

Also, if you want to define your own, the `Arb.filter`, `Arb.convert` and `Arb.mapFilter` functions will come in handy.
    
## Running tests using only modules

Since Arbitrary instances are given as static members of classes, and properties can be grouped together 
as static members of classes, and since top level let functions are compiled as static member of their 
enclosing module (which is compiled as a class), you can simply define your properties and generators as 
top level let-bound functions, and then register all generators and and all properties at once using the following trick:*)

(***define-output:Marker***)
let myprop (i:int) = i >= 0
let mygen = Arb.Default.Int32() |> Arb.mapFilter (fun i -> Math.Abs i) (fun i -> i >= 0)
let helper = "a string"
let private helper' = true

type Marker = class end
Arb.registerByType (typeof<Marker>.DeclaringType)
Check.QuickAll (typeof<Marker>.DeclaringType)

(***include-output:Marker***)

(**
The Marker type is just any type defined in the module, to be able to get to the module's Type. F# offers no way to get to a module's Type directly.

FsCheck determines the intent of the function based on its return type:

* Properties: public functions that return unit, bool, Property or function of any arguments to those types 
or Lazy value of any of those types. So `myprop` is the only property that is run; `helper'` also returns bool but is private.
* Arbitrary instances: return Arbitrary<_>

All other functions are respectfully ignored. If you have top level functions that return types that FsCheck will 
do something with, but do not want them checked or registered, just make them private. FsCheck will ignore those functions.


## Implementing IRunner to integrate FsCheck with mb|x|N|cs|Unit

The `Config` type that can be passed to the `Check.One` or `Check.All` methods takes an `IRunner` as argument. This interface has the following methods:

* `OnStartFixture` is called when FsCheck is testing all the methods on a type, before starting any tests.
* `OnArguments` is called after every test, passing the implementation the test number, the arguments and the every function. 
* `OnShrink` is called at every succesful shrink.
* `OnFinished` is called with the name of the test and the outcome of the overall test run. This is used in the example below to call Assert statements from an outside unit testing framework - allowing FsCheck to integrate with a number of unit testing frameworks. You can leverage another unit testing framework's ability to setup and tear down tests, have a nice graphical runner etc.*)

open Xunit

let xUnitRunner =
  { new IRunner with
      member x.OnStartFixture t = ()
      member x.OnArguments (ntest,args, every) = ()
      member x.OnShrink(args, everyShrink) = ()
      member x.OnFinished(name,testResult) = 
          match testResult with 
          | TestResult.True _ -> Assert.True(true)
          | _ -> Assert.True(false, Runner.onFinishedToString name testResult) 
  }
   
let withxUnitConfig = { Config.Default with Runner = xUnitRunner }

(**
## Implementing IRunner to customize printing of generated arguments

By default, FsCheck prints generated arguments using `sprintf "%A"`, or structured formatting. This usually does what you expect, 
i.e. for primitive types the value, for objects the ToString override and so on. If it does not (A motivating case is 
testing with COM objects - overriding ToString is not an option and structured formatting does not do anything useful with it), 
you can use the `label` combinator to solve this on a per property basis, but a more structured solution can be achieved by 
implementing `IRunner`. For example:*)
    
let formatterRunner formatter =
  { new IRunner with
      member x.OnStartFixture t =
          printf "%s" (Runner.onStartFixtureToString t)
      member x.OnArguments (ntest,args, every) =
          printf "%s" (every ntest (args |> List.map formatter))
      member x.OnShrink(args, everyShrink) =
          printf "%s" (everyShrink (args |> List.map formatter))
      member x.OnFinished(name,testResult) = 
          let testResult' = match testResult with 
                              | TestResult.False (testData,origArgs,shrunkArgs,outCome,seed) -> 
                                  TestResult.False (testData,origArgs |> List.map formatter, shrunkArgs |> List.map formatter,outCome,seed)
                              | t -> t
          printf "%s" (Runner.onFinishedToString name testResult') 
  }

(**    
## An equality comparison that prints the left and right sides of the equality

Properties commonly check for equality. If a test case fails, FsCheck prints the counterexample, but 
sometimes it is useful to print the left and right side of the comparison as well, especially if you 
do some complicated calculations with the generated arguments first. To make this easier, you can 
define your own labelling equality combinator:*)

(***define-output:testCompare***)
let (.=.) left right = left = right |@ sprintf "%A = %A" left right

let testCompare (i:int) (j:int) = 2*i+1  .=. 2*j-1
Check.Quick testCompare

(***include-output:testCompare***)

(**
Of course, you can do this for any operator or function that you often use.
    
## Some ways to run FsCheck tests

* By adding properties and generators to an fsx file in your project. It's easy to execute, just press ctrl-a and alt-enter, and the results are displayed in F# Interactive. Be careful when referencing dlls that are built in your solution; F# Interactive will lock those for the remainder of the session, and you won't be able to build unitl you quit the session. One solution is to include the source files instead of the dlls, but that makes the process slower. Useful for smaller projects. Difficult to debug though.
* By making a separate console application. Easy to debug, no annoying locks on assemblies. Your best option if you use only FsCheck for testing and your properties span multiple assemblies.
* By using another unit testing framework. Useful if you have a mixed FsCheck/unit testing approach (some things are easier to check using unit tests, and vice versa), and you like a graphical runner. Depending on what unit testing framework you use, you may get good integration with Visual Studio for free. See above for ways to customize FsCheck for this scenario.
*)