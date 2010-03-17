
#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/environment.h>
#include <mono/metadata/exception.h>
#include <stdlib.h>
#include <string.h>

MonoType *mono_reflection_type_get_handle (MonoReflectionType *ref);

static MonoClass *lslStringClass = NULL;
static MonoClassField *lslStringValueField = NULL;
static MonoMethod *lslStringCtorString = NULL;

static MonoException *NewBoxedLSLString (MonoDomain *domain, gunichar2 *buf, int len, MonoObject **lslStrOut);
static MonoString *BoxedLSLString2SysString (MonoObject *lslString);


/**
 * @brief Initialization routine
 *    int rc = xmrHelperInitialize (typeof (LSL_String));
 */
int XMRHelperInitialize (MonoDelegate *delegate, MonoReflectionType *lslStringRefType)
{
	gpointer iter1, iter2;
	MonoMethod *meth;
	MonoMethodSignature *sig;
	MonoType *type;

	/*
	 * Get LSL_String class descriptor.
	 * Get LSL_String m_String field descriptor.
	 */
	MonoType *lslStringType = mono_reflection_type_get_handle (lslStringRefType);
	lslStringClass = mono_class_from_mono_type (lslStringType);
	lslStringValueField = mono_class_get_field_from_name (lslStringClass, "m_string");
	g_assert (lslStringValueField != NULL);

	/*
	 * Get LSL_String constructor (with single string argument).
	 */
	iter1 = NULL;
	while ((meth = mono_class_get_methods (lslStringClass, &iter1)) != NULL) {
		printf ("XMRHelperInitialize*: LSL_String meth %s\n", mono_method_get_name (meth));
		if (strcmp (mono_method_get_name (meth), ".ctor") == 0) {
			sig = mono_method_signature (meth);
			printf ("XMRHelperInitialize*:   nargs %d\n", mono_signature_get_param_count (sig));
			if (mono_signature_get_param_count (sig) == 1) {
				iter2 = NULL;
				type = mono_signature_get_params (sig, &iter2);
				if (mono_class_from_mono_type (type) == mono_get_string_class ()) {
					lslStringCtorString = meth;
					break;
				}
			}
		}
	}
	g_assert (lslStringCtorString != NULL);

	return 0;
}

/**
 * @brief Helper for llParseString2List() to avoid boatloads of little shitty mallocs & memcpys.
 *
 * delegate Exception ParseString2ListDel (string str, 
 *                                         object[] separators, 
 *                                         object[] spacers, 
 *                                         bool keepNulls, 
 *                                         ref object[] outArray);
 * ParseString2ListDel parseString2List = (ParseString2ListDel)MMRDLOpen.GetDelegate ("xmrhelpers.so", 
 *                                                                                    "ParseString2List", 
 *                                                                                    typeof (ParseString2ListDel), 
 *                                                                                    null);
 *
 * LSL_List llParseString2List (LSL_String str, LSL_List separators, LSL_List spacers)
 * {
 *    object[] outArray;
 *    Exception e = parseString2List (str.m_string, separators.Data, spacers.Data, false, out outArray);
 *    if (e != null) throw e;
 *    return new LSL_List (outArray);
 * }
 */
MonoException *ParseString2List (MonoDelegate *delegate,
                                 MonoString *str, 
                                 MonoArray *separators, 
                                 MonoArray *spacers, 
                                 int keepNulls, 
                                 MonoArray **outArray)
{
	gunichar2 *startGood, *strPtr;
	int j, nOutput, strLen;
	MonoArray *out;
	MonoDomain *domain = mono_domain_get ();
	MonoException *except;
	MonoObject **outputs;

	*outArray = NULL;

	strLen = str->length;
	strPtr = str->chars;

	/*
	 * Array to hold output strings.
	 * Alloc big enough for maximum possible.
	 */
	nOutput = strLen;
	if (keepNulls) nOutput *= 2;
	out = mono_array_new (domain, mono_get_object_class (), nOutput);
	if (out == NULL) goto oom;
	outputs = (void *)(out->vector);
	nOutput = 0;

	/*
	 * First output string starts at char 0 of input.
	 */
	startGood = strPtr;
	while (strLen > 0) {

		/*
		 * Check for matching separator.
		 */
		for (j = 0; j < separators->max_length; j ++) {
			MonoObject *sepLSLStr = mono_array_get (separators, MonoObject *, j);
			MonoString *sepStr = BoxedLSLString2SysString (sepLSLStr);
			if (sepStr == NULL) continue;
			int sepStrLen = sepStr->length;
			if ((sepStrLen > 0) && 
			    (sepStrLen <= strLen) && 
			    (memcmp (sepStr->chars, strPtr, sepStrLen * sizeof sepStr->chars[0]) == 0)) {

				/*
				 * Separator matches, output string since end of last separator/spacer
				 * to beginning of this separator.
				 */
				if ((strPtr > startGood) || keepNulls) {
					except = NewBoxedLSLString (domain, startGood, strPtr - startGood, &outputs[nOutput++]);
					if (except != NULL) goto err;
				}

				/*
				 * Skip over the separator and resume searching after the separator.
				 */
				strLen -= sepStrLen;
				strPtr += sepStrLen;
				startGood = strPtr;
				goto nextstrchar;
			}
		}

		/*
		 * Check for matching spacer.
		 */
		for (j = 0; j < spacers->max_length; j ++) {
			MonoObject *spaLSLStr = mono_array_get (spacers, MonoObject *, j);
			MonoString *spaStr = BoxedLSLString2SysString (spaLSLStr);
			if (spaStr == NULL) continue;
			int spaStrLen = spaStr->length;
			if ((spaStrLen > 0) && 
			    (spaStrLen <= strLen) && 
			    (memcmp (spaStr->chars, strPtr, spaStrLen * sizeof spaStr->chars[0]) == 0)) {

				/*
				 * Spacer matches, output string since end of last separator/spacer
				 * to beginning of this spacer.
				 */
				if ((strPtr > startGood) || keepNulls) {
					except = NewBoxedLSLString (domain, startGood, strPtr - startGood, &outputs[nOutput++]);
					if (except != NULL) goto err;
				}

				/*
				 * Output the spacer.
				 */
				outputs[nOutput++] = spaLSLStr;

				/*
				 * Skip over the spacer and resume searching after the spacer.
				 */
				strLen -= spaStrLen;
				strPtr += spaStrLen;
				startGood = strPtr;
				goto nextstrchar;
			}
		}

		/*
		 * Didn't find it anywhere, check next input character.
		 */
		strLen --;
		strPtr ++;
nextstrchar:;
	}

	/*
	 * Maybe there's a little string on the end.
	 */
	if (strPtr > startGood) {
		except = NewBoxedLSLString (domain, startGood, strPtr - startGood, &outputs[nOutput++]);
		if (except != NULL) goto err;
	}

	/*
	 * Now chop array off at the number of elements actually used.
	 */
	g_assert (nOutput <= out->max_length);
	out->max_length = nOutput;

	/*
	 * Successful.
	 */
	*outArray = out;
	return NULL;

oom:
	except = mono_get_exception_out_of_memory ();
err:
	return except;
}

/**
 * @brief Create a boxed LSLString from an array of unicode characters.
 */
static MonoException *NewBoxedLSLString (MonoDomain *domain, gunichar2 *buf, int len, MonoObject **lslStrOut)
{
	gpointer args[1];
	MonoObject *except, *lslString;
	MonoString *sysString;

	/*
	 * Create system string out of the array of unicode characters.
	 */
	sysString = mono_string_new_utf16 (domain, buf, len);
	if (sysString == NULL) return mono_get_exception_out_of_memory ();
	{
		MonoClass *sysStringClass = mono_object_get_class ((MonoObject *)sysString);
		MonoType *sysStringType = mono_class_get_type (sysStringClass);
		g_assert (mono_type_get_type (sysStringType) == MONO_TYPE_STRING);
	}

	/*
	 * Create a boxed LSL_String struct, ie, create an LSL_String object.
	 */
	*lslStrOut = lslString = mono_object_new (domain, lslStringClass);
	if (lslString == NULL) return mono_get_exception_out_of_memory ();
	g_assert (mono_object_get_class (lslString) == lslStringClass);

	/*
	 * Call its constructor, passing it the system string.
	 */
	args[0] = sysString;
	except = NULL;
	mono_runtime_invoke (lslStringCtorString, mono_object_unbox (lslString), args, &except);
	if (except == NULL) {
		g_assert (mono_object_get_class (lslString) == lslStringClass);
		g_assert (BoxedLSLString2SysString (lslString) == sysString);
	}
	return (MonoException *)except;
}


/**
 * @brief Given an object, if it is a boxed LSL_String, return the mono string (else return NULL)
 */
static MonoString *BoxedLSLString2SysString (MonoObject *lslString)
{
	MonoObject *sysString = NULL;

	if (mono_object_get_class (lslString) == lslStringClass) {
		mono_field_get_value (lslString, lslStringValueField, &sysString);
		if (sysString != NULL) {
			MonoClass *sysStringClass = mono_object_get_class (sysString);
			MonoType *sysStringType = mono_class_get_type (sysStringClass);
			g_assert (mono_type_get_type (sysStringType) == MONO_TYPE_STRING);
		}
	}
	return (MonoString *)sysString;
}
