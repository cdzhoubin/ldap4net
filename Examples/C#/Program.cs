﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LdapForNet;
using static LdapForNet.Native.Native;

namespace LdapExample
{
    class Program
    {
        /// <summary>
        /// LdapSearch
        /// </summary>
        /// <example>
        ///  LdapExample --auth=GSSAPI --host=v04.example.com --base="dc=v04,dc=example,dc=com" --filter="(objectclass=*)"
        /// </example>
        ///  <example>
        ///  LdapExample --auth=Simple --host=ldap.forumsys.com --base="dc=example,dc=com" --filter="(objectclass=*)" --who="cn=read-only-admin,dc=example,dc=com" --password=password
        /// </example>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            var cmds = ParseCommandLine(args);
            cmds.TryGetValue("host", out var host);
            cmds.TryGetValue("auth", out var authString);
            cmds.TryGetValue("base", out var @base);
            cmds.TryGetValue("filter", out var filter);
            var auth = authString == LdapAuthMechanism.GSSAPI ? LdapAuthMechanism.GSSAPI : LdapAuthMechanism.SIMPLE;
            host = host ?? "ldap.forumsys.com";
            @base = @base ?? "dc=example,dc=com";
            filter = filter ?? "(objectclass=*)";
            try
            {
                UsingOpenLdap(auth, host, @base, filter, cmds);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            Console.WriteLine("End");
        }

        private static Dictionary<string, string> ParseCommandLine(string[] args)
        {
            var pattern = "^--([^=\"]*)=\"?(.*)\"?$";
            return args.Select(_ => Regex.Matches(_, pattern, RegexOptions.IgnoreCase).FirstOrDefault()?.Groups)
                .Where(_ => _ != null)
                .ToDictionary(_ => _[1].Value, _ => _[2].Value);
        }

        private static void UsingOpenLdap(string authType, string host, string @base, string filter, IDictionary<string, string> cmds)
        {
            using (var cn = new LdapConnection())
            {
                cn.Connect(host);
                if (authType == LdapAuthMechanism.GSSAPI)
                {
                    cn.Bind();
                }
                else
                {
                    cmds.TryGetValue("who", out var who);
                    cmds.TryGetValue("password", out var password);
                    who = who ?? "cn=read-only-admin,dc=example,dc=com";
                    password = password ?? "password";
                    cn.Bind(LdapAuthMechanism.SIMPLE,who,password);
                }

                IList<LdapEntry> entries = new List<LdapEntry>();

                if (cmds.TryGetValue("sid", out var sid))
                {
                    entries = cn.SearchBySid(@base, sid);
                }
                else
                {
                    entries = cn.Search(@base, filter);
                }
                foreach (var ldapEntry in entries)
                {
                    PrintEntry(ldapEntry);
                }
            }
        }

        private static void PrintEntry(LdapEntry entry)
        {
            Console.WriteLine($"dn: {entry.Dn}");
            foreach (var pair in entry.Attributes.SelectMany(_ => _.Value.Select(x => new KeyValuePair<string, string>(_.Key, x))))
            {
                Console.WriteLine($"{pair.Key}: {pair.Value}");
            }
            Console.WriteLine();
        }
    }
}