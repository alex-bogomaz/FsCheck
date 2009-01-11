﻿#light

namespace FsCheck

[<AutoOpen>]
module Generator

open Random
open Reflect
open System
open System.Reflection
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open TypeClass
 
type internal IGen = 
    abstract AsGenObject : Gen<obj>
    
///Generator of a random value, based on a size parameter and a randomly generated int.
and Gen<'a> = 
    Gen of (int -> StdGen -> 'a) 
        ///map the given function to the value in the generator, yielding a new generator of the result type.  
        member x.Map<'a,'b> (f: 'a -> 'b) : Gen<'b> = match x with (Gen g) -> Gen (fun n r -> f <| g n r)
    interface IGen with
        member x.AsGenObject = x.Map box
        //match x with (Gen g) -> Gen (fun n r -> g n r |> box)

///Apply ('map') the function f on the value in the generator, yielding a new generator.
let fmap f (gen:Gen<_>) = gen.Map f

///Obtain the current size. sized g calls g, passing it the current size as a parameter.
let sized fgen = Gen (fun n r -> let (Gen m) = fgen n in m n r)

///Override the current size of the test. resize n g invokes generator g with size parameter n.
let resize n (Gen m) = Gen (fun _ r -> m n r)

///Default generator that generates a random number generator. Useful for starting off the process
///of generating a random value.
let rand = Gen (fun n r -> r)

///Generates a value out of the generator with maximum size n.
let generate n rnd (Gen m) = 
    let size,rnd' = range (0,n) rnd
    m size rnd'

///The workflow type for generators.
type GenBuilder () =
    member b.Return(a) : Gen<_> = 
        Gen (fun n r -> a)
    member b.Bind((Gen m) : Gen<_>, k : _ -> Gen<_>) : Gen<_> = 
        Gen (fun n r0 -> let r1,r2 = split r0
                         let (Gen m') = k (m n r1) 
                         m' n r2)                                      
    //member b.Let(p, rest) : Gen<_> = rest p
    //not so sure about this one...should delay executing until just before it is executed,
    //for side-effects. Examples are usually like = fun () -> runGen (f ())
    member b.Delay(f : unit -> Gen<_>) : Gen<_> = 
        Gen (fun n r -> match f() with (Gen g) -> g n r )
    member b.TryFinally(Gen m,handler ) = 
        Gen (fun n r -> try m n r finally handler)
    member b.TryWith(Gen m, handler) = 
        Gen (fun n r -> try m n r with e -> handler e)
    member b.Using (a, k) =  //'a * ('a -> Gen<'b>) -> Gen<'b> when 'a :> System.IDisposable
        use disposea = a
        k disposea

///The workflow function for generators, e.g. gen { ... }
let gen = GenBuilder()

///Generates an integer between l and h, inclusive.
///Note to QuickCheck users: this function is more general in QuickCheck, generating a Random a.
let choose (l, h) = rand.Map (range (l,h) >> fst) 

///Build a generator that randomly generates one of the values in the given list.
let elements xs = (choose (0, (List.length xs)-1) ).Map(List.nth xs)

///Build a generator that generates a value from one of the generators in the given list, with
///equal probability.
let oneof gens = gen.Bind(elements gens, fun x -> x)

///Build a generator that generates a value from one of the generators in the given list, with
///given probabilities.
let frequency xs = 
    let tot = List.sum_by (fun x -> x) (List.map fst xs)
    let rec pick n ys = match ys with
                        | (k,x)::xs -> if n<=k then x else pick (n-k) xs
                        | _ -> raise (ArgumentException("Bug in frequency function"))
    in gen.Bind(choose (1,tot), fun n -> pick n xs)  

///Lift the given function over values to a function over generators of those values.
let liftGen f = fun a -> gen {  let! a' = a
                                return f a' }

///Lift the given function over values to a function over generators of those values.
let liftGen2 f = fun a b -> gen {   let! a' = a
                                    let! b' = b
                                    return f a' b' }
                                    
///Build a generator that generates a 2-tuple of the values generated by the given generator.
let two g = liftGen2 (fun a b -> (a,b)) g g

///Lift the given function over values to a function over generators of those values.
let liftGen3 f = fun a b c -> gen { let! a' = a
                                    let! b' = b
                                    let! c' = c
                                    return f a' b' c' }

///Build a generator that generates a 3-tuple of the values generated by the given generator.
let three g = liftGen3 (fun a b c -> (a,b,c)) g g g

///Lift the given function over values to a function over generators of those values.
let liftGen4 f = fun a b c d -> gen {   let! a' = a
                                        let! b' = b
                                        let! c' = c
                                        let! d' = d
                                        return f a' b' c' d' }

///Build a generator that generates a 4-tuple of the values generated by the given generator.
let four g = liftGen4 (fun a b c d -> (a,b,c,d)) g g g g

///Lift the given function over values to a function over generators of those values.
let liftGen5 f = fun a b c d e -> gen { let! a' = a
                                        let! b' = b
                                        let! c' = c
                                        let! d' = d
                                        let! e' = e
                                        return f a' b' c' d' e'}

///Lift the given function over values to a function over generators of those values.
let liftGen6 f = fun a b c d e g -> gen {   let! a' = a
                                            let! b' = b
                                            let! c' = c
                                            let! d' = d
                                            let! e' = e
                                            let! g' = g
                                            return f a' b' c' d' e' g'}

let private fraction (a:int) (b:int) (c:int) = 
    double a + ( double b / abs (double c)) + 1.0 

///Sequence the given list of generators into a generator of a list.
///Note to QuickCheck users: this is monadic sequence, which cannot be expressed generally in F#.
let rec sequence l = match l with
                            | [] -> gen { return [] }
                            | c::cs -> gen {let! x = c
                                            let! xs = sequence cs
                                            return  x::xs } 

///Generates a list of given length, containing values generated by the given generator.
///vector g n generates a list of n t's, if t is the type that g generates.
let vector arbitrary n = sequence [ for i in 1..n -> arbitrary ]

let private promote f = Gen (fun n r -> fun a -> let (Gen m) = f a in m n r)

///Basic co-arbitrary generator, which is dependent on an int.
let variant = fun v (Gen m) ->
    let rec rands r0 = seq { let r1,r2 = split r0 in yield! Seq.cons r1 (rands r2) } 
    Gen (fun n r -> m n (Seq.nth (v+1) (rands r)))


type IArbitrary =
    abstract GenObj : Gen<obj>


[<AbstractClass>]
type Arbitrary<'a>() =
    abstract Arbitrary : Gen<'a>
    abstract CoArbitrary : 'a -> (Gen<'c> -> Gen<'c>) 
    default x.CoArbitrary (_:'a) = failwithf "CoArbitrary for %A is not implemented" (typeof<'a>)
    interface IArbitrary with
        member x.GenObj = (x.Arbitrary :> IGen).AsGenObject

///Returns a Gen<'a>
let arbitrary<'a> = getInstance (typedefof<Arbitrary<_>>) (typeof<'a>) |> unbox<Arbitrary<'a>> |> (fun arb -> arb.Arbitrary)

///Returns a generator transformer for the given type, aka a coarbitrary function.
let coarbitrary (a:'a)  = 
    getInstance (typedefof<Arbitrary<_>>) (typeof<'a>) |> unbox<(Arbitrary<'a>)> |> (fun arb -> arb.CoArbitrary) <| a
    
newTypeClass<Arbitrary<_>>

//----------contirbution by Neil. Should be integrated with existing generators; currently given
//generators for chars etc are not honored, and neither are user defined primitive types.
let debugNeilCheck = false

// first function given is impure
//type NeilGen = (int -> int -> int) -> int -> obj

//let private genMap : Ref<Map<string, Lazy<NeilGen>>> = ref (Map.empty)

// Compute which types are possible children of this type
// Helps make union generation terminate quicker
let private containedTypes (t : Type) : list<Type> = [] // TODO

//let rec private getNeilGen (t : Type) : Lazy<NeilGen> =
//        let ts = t.ToString()
//        match (!genMap).TryFind ts with
//        | Some v -> v
//        | None ->
//            let res = lazy neilGen t
//            genMap := (!genMap).Add (ts,res)
//            res

let private getGenerator t = getInstance (typedefof<Arbitrary<_>>) t |> unbox<IArbitrary> |> (fun arb -> arb.GenObj)

//this finds the generators for each of the types, then chooses one element for each type (so, a product type like tuples)
let private productGen (ts : list<Type>) =
    let gs = [ for t in ts -> getGenerator t ]
    let n = gs.Length
    [ for g in gs -> sized (fun s -> resize ((s / n) - 1) (unbox<IGen> g).AsGenObject) ]
    //fun next size -> [| for g in gs -> g.Value next ((size / n) - 1) |]

//and intGen next size = next (-size) size 
//and charGen next size = Char.chr (next 32 127)

let neilGen (t : Type) = //: NeilGen =
    if t.IsArray then
        let t2 = t.GetElementType()
        let inner = getGenerator t2
        let toTypedArray (arrType:Type) (l:list<_>) = 
            let res = Array.CreateInstance(arrType, l.Length)
            List.iteri (fun i value -> res.SetValue(value,i)) l
            res
        let genArr s = vector inner s |> fmap (toTypedArray t2 >> box) //|> unbox<IGen> |> (fun g -> g.AsGenObject)
        box <| sized genArr
//        fun next size ->
//            let n = max 0 (next 0 size)
//            let res = Array.CreateInstance(t2, n)
//            for i in 0 .. n-1 do
//                res.SetValue(inner.Value next (size - 1), i)
//            box res

//    //this is for lists; based on the generator for arrays (turns an array into a list using reflection)
//    elif genericTypeEq t (typeof<List<unit>>) then
//        let t2 = (t.GetGenericArguments()).[0]
//        let inner = getNeilGen (t2.MakeArrayType())
//        
//        let modu = t.Assembly.GetType "Microsoft.FSharp.Collections.ListModule"
//        let meth = modu.GetMethod "of_array"
//        let of_array = meth.MakeGenericMethod [| t2 |]
//        
//        fun next size -> box <| of_array.Invoke(null, [| inner.Value next size |])

    
//    elif isTupleType t then
//        let ts = FSharpType.GetTupleElements t |> List.of_array
//        let g = productGen ts
//        let create = FSharpValue.PrecomputeRecordConstructor t
//        let result = g |> Array.to_list |> sequence |> fmap (List.to_array >> create)
//        box result
//        //fun next size -> create (g next size)

    elif isRecordType t then
        let g = productGen [ for pi in getRecordFields t -> pi.PropertyType ]
        let create = getRecordConstructor t
        let result = g |> sequence |> fmap (List.to_array >> create)
        box result
        //fun next size -> create (g next size)


    elif isUnionType t then
        // figure out the "size" of a union
        // 0 = nullary, 1 = non-recursive, 2 = recursive
        let unionSize (ts : list<Type>) : int =
            if ts.IsEmpty then 0 else
                let tsStar = List.concat (ts :: List.map containedTypes ts) //containedTypes is not implemented, always returns[]
                if List.exists(fun (x : Type) -> x.ToString() = t.ToString()) tsStar then 2 else 1
                //so this wil either return 0 or 1, never 2...
                
        let unionGen create ts =
            let g = productGen ts
            let res = g |> sequence |> fmap (List.to_array >> create)
            res
            //fun next size -> create (g next size)

        let gs = [ for _,(_,fields,create,_) in getUnionCases t -> unionSize fields, lazy (unionGen create fields) ]
        let lowest = List.reduce_left min <| List.map fst gs
        let small() = [ for i,g in gs do if i = lowest then yield g.Force() ]
        let large() = [ for _,g in gs -> g.Force() ]
        //fun next size ->
        let getgs size = 
            if size <= 0 then 
                let sm = small()
                resize (size / max 1 sm.Length) <| oneof sm
            else 
                let la = large()
                resize (size / max 1 la.Length) <| oneof la
        sized getgs |> box
        //gs.[next 0 (gs.Length-1)] next size


//    elif t = typeof<string> then
//        let inner = getNeilGen (typeof<char[]>)
//        fun next size -> box <| new String(unbox (inner.Value next size) : char[])
//
//    elif t = typeof<float> then
//        fun next size ->
//            let fraction a b c = double a + ( double b / abs (double c)) + 1.0 
//            let value() = intGen next size
//            box <| fraction (value()) (value()) (value())
//
//    elif t = typeof<unit> then
//        fun next size -> box ()
//    elif t = typeof<int> then
//        fun next size -> box <| intGen next size
//    elif t = typeof<char> then
//        fun next size -> box <| charGen next size
    else
        failwithf "Geneflect: type not handled %A" t

let private geneflectObj (t:Type) = (neilGen t |> unbox<IGen>).AsGenObject

//and private geneflectObj (t : Type) : Gen<obj> = Gen <| fun size stdgen ->
//    if debugNeilCheck then printfn "%A" size
//    let gen = ref stdgen
//    let next low high =
//        let v,g = range (low,high) !gen
//        gen := g
//        v
//    (getNeilGen t).Value next size

//let geneflect() : Gen<'a> = (geneflectObj (typeof<'a>)).Map unbox
//----------------end of contributed part-------------------

///A collection of default generators.
type Arbitrary() =
    ///Generates (), of the unit type.
    static member Unit() = 
        { new Arbitrary<unit>() with
            override x.Arbitrary = gen { return () } 
            override x.CoArbitrary _ = variant 0
        }
    ///Generates arbitrary bools.
    static member Bool() = 
        { new Arbitrary<bool>() with
            override x.Arbitrary = elements [true; false] 
            override x.CoArbitrary b = if b then variant 0 else variant 1
        }
    ///Generate arbitrary int that is between -size and size.
    static member Int() = 
        { new Arbitrary<int>() with
            override x.Arbitrary = sized <| fun n -> choose (-n,n) 
            override x.CoArbitrary n = variant (if n >= 0 then 2*n else 2*(-n) + 1)
        }
    ///Generates arbitrary floats, NaN included fairly frequently.
    static member Float() = 
        { new Arbitrary<float>() with
            override x.Arbitrary = liftGen3 fraction arbitrary arbitrary arbitrary
            override x.CoArbitrary fl = 
                let d1 = sprintf "%g" fl
                let spl = d1.Split([|'.'|])
                let m = if (spl.Length > 1) then spl.[1].Length else 0
                let decodeFloat = (fl * float m |> int, m )
                coarbitrary <| decodeFloat
                //Co.Tuple(Co.Int, Co.Int) <| decodeFloat
        }
    ///Generates arbitrary chars, between ASCII codes Char.MinValue and 127.
    static member Char() = 
        { new Arbitrary<char>() with
            override x.Arbitrary = fmap char (choose (int Char.MinValue, 127))
            override x.CoArbitrary c = coarbitrary (int c)
        }
    ///Generates arbitrary strings, which are lists of chars generated by Char.
    static member String() = 
        { new Arbitrary<string>() with
            override x.Arbitrary = fmap (fun chars -> new String(List.to_array chars)) arbitrary
            override x.CoArbitrary (s:string) = s.ToCharArray() |> Array.to_list |> coarbitrary //Co.List (Co.Char) s
        }
    ///Genereate a 2-tuple.
    static member Tuple2() = 
        { new Arbitrary<'a*'b>() with
            override x.Arbitrary = liftGen2 (fun x y -> (x,y)) arbitrary arbitrary
            //extra parametrs are needed here, otherwise F# gets confused about the number of arguments
            //and doesn't correctly see that this really overriddes the right method
            override x.CoArbitrary ((a,b)) = coarbitrary a >> coarbitrary b//match t with (a,b) -> coarbitrary a >> coarbitrary b
        }
    ///Genereate a 3-tuple.
    static member Tuple3() = 
        { new Arbitrary<'a*'b*'c>() with
            override x.Arbitrary = liftGen3 (fun x y z -> (x,y,z)) arbitrary arbitrary arbitrary
            override x.CoArbitrary ((a,b,c)) = coarbitrary a >> coarbitrary b >> coarbitrary c
        }
    ///Genereate a 4-tuple.
    static member Tuple4() = 
        { new Arbitrary<'a*'b*'c*'d>() with
            override x.Arbitrary = liftGen4 (fun x y z u-> (x,y,z,u)) arbitrary arbitrary arbitrary arbitrary
            override x.CoArbitrary ((a,b,c,d)) = coarbitrary a >> coarbitrary b >> coarbitrary c >> coarbitrary d
        }
    ///Genereate a 5-tuple.
    static member Tuple5() = 
        { new Arbitrary<'a*'b*'c*'d*'e>() with
            override x.Arbitrary = liftGen5 (fun x y z u v-> (x,y,z,u,v)) arbitrary arbitrary arbitrary arbitrary arbitrary
            override x.CoArbitrary ((a,b,c,d,e)) = coarbitrary a >> coarbitrary b >> coarbitrary c >> coarbitrary d >> coarbitrary e
        }
    ///Genereate a 6-tuple.
    static member Tuple6() = 
        { new Arbitrary<'a*'b*'c*'d*'e*'f>() with
            override x.Arbitrary = liftGen6 (fun x y z u v w-> (x,y,z,u,v,w)) arbitrary arbitrary arbitrary arbitrary arbitrary arbitrary
            override x.CoArbitrary ((a,b,c,d,e,f)) = coarbitrary a >> coarbitrary b >> coarbitrary c >> coarbitrary d >> coarbitrary e >> coarbitrary f
        }
    ///Generate an option value that is 'None' 1/4 of the time.
    static member Option() = 
        { new Arbitrary<option<'a>>() with
            override x.Arbitrary = frequency [(1, gen { return None }); (3, liftGen Some arbitrary)]
            override x.CoArbitrary o = 
                match o with 
                | None -> variant 0
                | Some y -> variant 1 >> coarbitrary y 
        }
    ///Generate a list of values. The size of the list is between 0 and the test size + 1.
    static member FsList() = 
        { new Arbitrary<list<'a>>() with
            override x.Arbitrary = sized (fun n -> gen.Bind(choose(0,n+1 (*avoid empties*)), vector arbitrary))
            override x.CoArbitrary l = 
                match l with
                | [] -> variant 0
                | x::xs -> coarbitrary x << variant 1 << coarbitrary xs
        }
//    static member Array() =
//        { new Arbitrary<'a[]>() with
//            override x.Arbitrary = arbitrary |> fmap List.to_array
//            //TODO: coarbitrary
//        }
     ///Generate a function value.
    static member Arrow() = 
        { new Arbitrary<'a->'b>() with
            override x.Arbitrary = promote (fun a -> coarbitrary a arbitrary)
            override x.CoArbitrary f gen = 
                gen {   let x = arbitrary
                        return! coarbitrary (fmap f x) gen } 
        }
    static member CatchAll() =
        { new Arbitrary<'a>() with
            override x.Arbitrary = fmap (unbox<'a>) (geneflectObj (typeof<'a>))
        }
        
do registerInstances<Arbitrary<_>,Arbitrary>()

let registerGenerators<'t>() = registerInstances<Arbitrary<_>,'t>()
let overwriteGenerators<'t>() = overwriteInstances<Arbitrary<_>,'t>()
              


//and internal getGenerator (genericMap:IDictionary<_,_>) (t:Type)  =
//    if t.IsGenericParameter then
//        //special code for when a generic parameter needs to be generated
//        Gen.Object |> box
//        //the code below chooses one generator type per generic type and then sticks with it
//        //however, because of the difference in behavior between methodinfo.GetGenericArgs (return a name for
//        //generic args) and FSharpType.GetFunctionElements (returns obj for generic args), the code below
//        //causes a discrepancy. Now both will generate Whatever<obj>. 
////        if genericMap.ContainsKey(t) then 
////            genericMap.[t]
////        else
////            let newGenerator =  
////                [ Gen.Unit |> box;
////                  Gen.Bool |> box;
////                  Gen.Char |> box;
////                  Gen.String |> box ]
////                |> elements
////                |> generate 0 (newSeed())
////            genericMap.Add(t, newGenerator)
////            newGenerator 
//    else
//        match generators.TryGetValue(t) with 
//        |(true, mi) -> mi.Invoke(null, null) //we found a specific generator, use that
//        |(false, _) -> 
//            if t.IsGenericType then
//                match generators.TryGetValue(t.GetGenericTypeDefinition()) with
//                |(true, mi) -> 
//                    //found a generic generator
//                    let args = t.GetGenericArguments() |> Array.map (getGenerator genericMap)
//                    let typeargs = args |> Array.map (fun o -> o.GetType().GetGenericArguments().[0])
//                    let mi = if mi.ContainsGenericParameters then mi.MakeGenericMethod(typeargs) else mi
//                    mi.Invoke(null, args)
//                |(false, _) -> //we got nothing. Geneflect the thing.
//                    geneflectObj t |> box 
//            else
//                geneflectObj t |> box