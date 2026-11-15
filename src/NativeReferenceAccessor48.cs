using System;
using System.Reflection;
using System.Reflection.Emit;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    // Fixed reference-field ABI only. No value-type boxing, params arrays or
    // per-frame metadata search. Older Mono may reject LCG; cache FieldInfo as
    // the compatible fallback rather than disabling native camera following.
    internal sealed class NativeReferenceAccessor48<TTarget,TValue>
        where TTarget:class where TValue:class
    {
        readonly FieldInfo Field;
        Func<TTarget,TValue> Getter;
        Action<TTarget,TValue> Setter;
        readonly bool Writable;
        internal bool TypedRead {get{return Getter!=null;}}
        internal bool TypedWrite {get{return Setter!=null;}}
        internal NativeReferenceAccessor48(string name,bool writable,bool allowDynamic)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
            for(Type type=typeof(TTarget);type!=null;type=type.BaseType)
            {Field=type.GetField(name,flags);if(Field!=null)break;}
            if(Field==null||Field.FieldType!=typeof(TValue)||writable&&Field.IsInitOnly)
                throw new MissingFieldException(typeof(TTarget).FullName,name+" (reference ABI)");
            Writable=writable;if(!allowDynamic)return;
            try{
                DynamicMethod read=new DynamicMethod("EF48_Get_"+name,typeof(TValue),new Type[]{typeof(TTarget)},Field.DeclaringType,true);
                ILGenerator il=read.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,Field);il.Emit(OpCodes.Ret);
                Getter=(Func<TTarget,TValue>)read.CreateDelegate(typeof(Func<TTarget,TValue>));
                if(writable){
                    DynamicMethod write=new DynamicMethod("EF48_Set_"+name,typeof(void),new Type[]{typeof(TTarget),typeof(TValue)},Field.DeclaringType,true);
                    il=write.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Stfld,Field);il.Emit(OpCodes.Ret);
                    Setter=(Action<TTarget,TValue>)write.CreateDelegate(typeof(Action<TTarget,TValue>));
                }
            }catch(NotSupportedException){Getter=null;Setter=null;}
            catch(MemberAccessException){Getter=null;Setter=null;}
            catch(System.Security.SecurityException){Getter=null;Setter=null;}
            catch(InvalidProgramException){Getter=null;Setter=null;}
        }
        internal TValue Read(TTarget target)
        {
            if(Getter!=null){try{return Getter(target);}catch(MemberAccessException){Getter=null;}catch(InvalidProgramException){Getter=null;}
                catch(NotSupportedException){Getter=null;}catch(System.Security.SecurityException){Getter=null;}}
            return (TValue)Field.GetValue(target);
        }
        internal void Write(TTarget target,TValue value)
        {
            if(!Writable)throw new InvalidOperationException("Read-only native reference accessor");
            if(Setter!=null){try{Setter(target,value);return;}catch(MemberAccessException){Setter=null;}catch(InvalidProgramException){Setter=null;}
                catch(NotSupportedException){Setter=null;}catch(System.Security.SecurityException){Setter=null;}}
            Field.SetValue(target,value);
        }
    }
}
