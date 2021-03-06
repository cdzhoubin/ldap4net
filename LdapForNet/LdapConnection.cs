﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LdapForNet.Native;
using LdapForNet.Utils;
using static LdapForNet.Native.Native;

namespace LdapForNet
{
    public class LdapConnection: ILdapConnection
    {
        private IntPtr _ld = IntPtr.Zero;
        private bool _bound;
        
        public void Connect(string hostname, int port = (int)LdapPort.LDAP, LdapVersion version = LdapVersion.LDAP_VERSION3)
        {
            ThrowIfError(
                ldap_initialize(ref _ld, $"LDAP://{hostname}:{port}"),
                nameof(ldap_initialize)
            );
            var ldapVersion = (int)version;
            ThrowIfError(
                ldap_set_option(_ld, (int)LdapOption.LDAP_OPT_PROTOCOL_VERSION, ref ldapVersion),
                nameof(ldap_set_option)
            );
        }

        public void Bind(string mechanism = LdapAuthMechanism.GSSAPI, string userDn = null, string password = null)
        {
            ThrowIfNotInitialized();
            if (LdapAuthMechanism.SIMPLE.Equals(mechanism,StringComparison.OrdinalIgnoreCase))
            {
                SimpleBind(userDn,password);
            }
            else if (LdapAuthMechanism.GSSAPI.Equals(mechanism,StringComparison.OrdinalIgnoreCase))
            {
                GssApiBind();
            }
            else
            {
                throw new LdapException($"Not implemented mechanism: {mechanism}. Available: {LdapAuthMechanism.GSSAPI} | {LdapAuthMechanism.SIMPLE}. ");
            }

            _bound = true;
        }

        public void SetOption(LdapOption option, int value)
        {
            ThrowIfNotBound();
            ThrowIfError(ldap_set_option(_ld, (int)option, ref value),nameof(ldap_set_option));
        }
        
        public void SetOption(LdapOption option, string value)
        {
            ThrowIfNotBound();
            ThrowIfError(ldap_set_option(_ld, (int)option, ref value),nameof(ldap_set_option));
        }
        
        public void SetOption(LdapOption option, IntPtr valuePtr)
        {
            ThrowIfNotBound();
            ThrowIfError(ldap_set_option(_ld, (int)option, valuePtr),nameof(ldap_set_option));
        }
        
        public IList<LdapEntry> Search(string @base, string filter, LdapSearchScope scope = LdapSearchScope.LDAP_SCOPE_SUBTREE)
        {
            ThrowIfNotBound();
            var msg = Marshal.AllocHGlobal(IntPtr.Size);

            var res = ldap_search_ext_s(
                _ld, 
                @base, 
                (int)scope,
                filter,
                null,
                (int)LdapSearchAttributesOnly.False,
                IntPtr.Zero, 
                IntPtr.Zero, 
                IntPtr.Zero, 
                (int)LdapSizeLimit.LDAP_NO_LIMIT,
                ref msg);

            
            if (res != (int)LdapResultCode.LDAP_SUCCESS)
            {
                Marshal.FreeHGlobal(msg);
                ThrowIfError(_ld, res,nameof(ldap_search_ext_s));
            }

            var ber = Marshal.AllocHGlobal(IntPtr.Size);

            var ldapEntries = GetLdapEntries(_ld, msg, ber).ToList();

            Marshal.FreeHGlobal(ber);
            ldap_msgfree(msg);

            return ldapEntries;
        }

        public void Add(LdapEntry entry)
        {
            ThrowIfNotBound();
            if (string.IsNullOrWhiteSpace(entry.Dn))
            {
                throw new ArgumentNullException(nameof(entry.Dn));
            }

            if (entry.Attributes == null)
            {
                entry.Attributes = new Dictionary<string, List<string>>();
            }

            var attrs = entry.Attributes.Select(ToLdapMod).ToList();
            
            var ptr = Marshal.AllocHGlobal(IntPtr.Size*(attrs.Count+1)); // alloc memory for list with last element null
            MarshalUtils.StructureArrayToPtr(attrs,ptr, true);

            try
            {
                ThrowIfError(_ld, ldap_add_ext_s(_ld,
                    entry.Dn,
                    ptr,                
                    IntPtr.Zero, 
                    IntPtr.Zero 
                ), nameof(ldap_add_ext_s));

            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
                attrs.ForEach(_ => { Marshal.FreeHGlobal(_.mod_vals_u.modv_strvals); });
            }
        }

        public void Modify(LdapModifyEntry entry)
        {
            ThrowIfNotBound();
            
            if (string.IsNullOrWhiteSpace(entry.Dn))
            {
                throw new ArgumentNullException(nameof(entry.Dn));
            }
            
            if (entry.Attributes == null)
            {
                entry.Attributes = new List<LdapModifyAttribute>();
            }
            
            var attrs = entry.Attributes.Select(ToLdapMod).ToList();
            
            var ptr = Marshal.AllocHGlobal(IntPtr.Size*(attrs.Count+1)); // alloc memory for list with last element null
            MarshalUtils.StructureArrayToPtr(attrs,ptr, true);

            try
            {
                ThrowIfError(_ld, ldap_modify_ext_s(_ld,
                    entry.Dn,
                    ptr,                
                    IntPtr.Zero, 
                    IntPtr.Zero 
                ), nameof(ldap_modify_ext_s));

            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
                attrs.ForEach(_ => { Marshal.FreeHGlobal(_.mod_vals_u.modv_strvals); });
            }
        }

        public void Dispose()
        {
            if (_ld != IntPtr.Zero)
            {
                TraceIfError(ldap_unbind_s(_ld),nameof(ldap_unbind_s));
            }
        }

        public IntPtr GetNativeLdapPtr()
        {
            return _ld;
        }


        public void Delete(string dn)
        {
            ThrowIfNotBound();
            if (string.IsNullOrWhiteSpace(dn))
            {
                throw new ArgumentNullException(nameof(dn));
            }
            ThrowIfError(_ld, ldap_delete_ext_s(_ld,
                dn,
                IntPtr.Zero, 
                IntPtr.Zero 
            ), nameof(ldap_delete_ext_s));
        }
        
        private static List<string> GetModValue(List<string> values)
        {
            var res = values??new List<string>();
            res.Add(null);
            return res;
        }
        
        private static LDAPMod ToLdapMod(KeyValuePair<string, List<string>> attribute)
        {
            return ToLdapMod(new LdapModifyAttribute
            {
                Type = attribute.Key,
                LdapModOperation = LDAP_MOD_OPERATION.LDAP_MOD_ADD,
                Values = attribute.Value
            });
        }
        
        private static LDAPMod ToLdapMod(LdapModifyAttribute attribute)
        {
            var modValue = GetModValue(attribute.Values);
            var modValuePtr = Marshal.AllocHGlobal(IntPtr.Size * (modValue.Count));
            MarshalUtils.StringArrayToPtr(modValue, modValuePtr);
            return new LDAPMod
            {
                mod_op = (int) attribute.LdapModOperation,
                mod_type = attribute.Type,
                mod_vals_u = new LDAPMod.mod_vals
                {
                    modv_strvals = modValuePtr,
                },
                mod_next = IntPtr.Zero
            };
        }
        private void GssApiBind()
        {
            var defaults = GetSaslDefaults(_ld);
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(defaults));
            Marshal.StructureToPtr(defaults, ptr, false);

            var res = ldap_sasl_interactive_bind_s(_ld, null, LdapAuthMechanism.GSSAPI, IntPtr.Zero, IntPtr.Zero,
                (uint)LdapInteractionFlags.LDAP_SASL_QUIET, (l, flags, d, interact) => (int)LdapResultCode.LDAP_SUCCESS, ptr);

            ThrowIfError(_ld, res,nameof(ldap_sasl_interactive_bind_s));
        }

        private static LdapSaslDefaults GetSaslDefaults(IntPtr ld)
        {
            var defaults = new LdapSaslDefaults { mech = LdapAuthMechanism.GSSAPI };
            ThrowIfError(ldap_get_option(ld, (int)LdapOption.LDAP_OPT_X_SASL_REALM, ref defaults.realm),nameof(ldap_get_option));
            ThrowIfError(ldap_get_option(ld, (int)LdapOption.LDAP_OPT_X_SASL_AUTHCID, ref defaults.authcid),nameof(ldap_get_option));
            ThrowIfError(ldap_get_option(ld, (int)LdapOption.LDAP_OPT_X_SASL_AUTHZID, ref defaults.authzid),nameof(ldap_get_option));
            return defaults;
        }
        private void SimpleBind(string userDn, string password)
        {
            ThrowIfError(
                _ld,
                ldap_simple_bind_s(_ld, userDn, password)
                ,nameof(ldap_simple_bind_s)
            );
        }


        private static IEnumerable<LdapEntry> GetLdapEntries(IntPtr ld, IntPtr msg, IntPtr ber)
        {
            for (var entry = ldap_first_entry(ld, msg); entry != IntPtr.Zero;
                entry = ldap_next_entry(ld, entry))
            {
                yield return new LdapEntry
                {
                    Dn = GetLdapDn(ld, entry),
                    Attributes = GetLdapAttributes(ld, entry, ref ber)
                };
            }
        }

        private static string GetLdapDn(IntPtr ld, IntPtr entry)
        {
            var ptr = ldap_get_dn(ld, entry);
            var dn = Marshal.PtrToStringAnsi(ptr);
            ldap_memfree(ptr);
            return dn;
        }

        private static Dictionary<string, List<string>> GetLdapAttributes(IntPtr ld, IntPtr entry, ref IntPtr ber)
        {
            var dict = new Dictionary<string, List<string>>();
            for (var attr = ldap_first_attribute(ld, entry, ref ber);
                attr != IntPtr.Zero;
                attr = ldap_next_attribute(ld, entry, ber))
            {
                var vals = ldap_get_values(ld, entry, attr);
                if (vals != IntPtr.Zero)
                {
                    dict.Add(Marshal.PtrToStringAnsi(attr), MarshalUtils.PtrToStringArray(vals));
                    ldap_value_free(vals);
                }

                ldap_memfree(attr);
            }

            return dict;
        }
        
        private void ThrowIfNotInitialized()
        {
            if (_ld == IntPtr.Zero)
            {
                throw new LdapException($"Not initialized connection. Please invoke {nameof(Connect)} method before.");
            }
        }

        private void ThrowIfNotBound()
        {
            ThrowIfNotInitialized();
            if (_bound == false)
            {
                throw new LdapException($"Not bound. Please invoke {nameof(Bind)} method before.");
            }
        }

        private static void ThrowIfError(int res, string method)
        {
            if (res != (int)LdapResultCode.LDAP_SUCCESS)
            {
                throw new LdapException(LdapError2String(res), method, res);
            }
        }

        private static void ThrowIfError(IntPtr ld, int res, string method)
        {
            if (res != (int)LdapResultCode.LDAP_SUCCESS)
            {
                var error = LdapError2String(res);
                var info = GetAdditionalErrorInfo(ld);
                var message = !string.IsNullOrWhiteSpace(info)? $"{error}. {info}": error;
                throw new LdapException(message, method, res);
            }
        }

        private static void TraceIfError(int res, string method)
        {
            if (res != (int)LdapResultCode.LDAP_SUCCESS)
            {
                Trace.TraceError($"Error {method}: {LdapError2String(res)} ({res}).");
            }
        }
    }
}
