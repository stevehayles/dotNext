using System;
using System.Reflection;
using static System.Runtime.ExceptionServices.ExceptionDispatchInfo;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace DotNext.Reflection
{
    using ConceptAttribute = Runtime.CompilerServices.ConceptAttribute;

    /// <summary>
    /// Provides a check of constaints defined by concept types.
    /// </summary>
    public static class Concept
    {
        /// <summary>
        /// Applies constraints described by concept type.
        /// </summary>
        /// <param name="conceptType">A static type describing concept.</param>
        /// <exception cref="ConstraintViolationException">One or more constaints defined by concept type are violated.</exception>
        /// <exception cref="ArgumentException"><paramref name="conceptType"/> is not marked with <see cref="ConceptAttribute"/>.</exception>
        public static void Assert(Type conceptType)
        {
            if(!conceptType.IsDefined<ConceptAttribute>())
                throw new ArgumentException(ExceptionMessages.ConceptTypeInvalidAttribution<ConceptAttribute>(conceptType), nameof(conceptType));
            try
            {
                //run class constructor for concept type and its parents
                while(!(conceptType is null))
                {
                    RunClassConstructor(conceptType.TypeHandle);
                    conceptType = conceptType.BaseType;
                }
            } 
            catch(TypeInitializationException e)
            {
                if(e.InnerException is ConstraintViolationException violation)
                    Capture(violation).Throw();
                throw;
            }
        }

        /// <summary>
        /// Applies a chain of constraints described by multiple concept types.
        /// </summary>
        /// <param name="conceptType">A static type describing concept.</param>
        /// <param name="other">A set of static types describing concept.</param>
        /// <exception cref="ConstraintViolationException">Constraints defined by concept types are violated.</exception>
        public static void Assert(Type conceptType, params Type[] other)
        {
            Assert(conceptType);
            Array.ForEach(other, Assert);
        }
    }
}