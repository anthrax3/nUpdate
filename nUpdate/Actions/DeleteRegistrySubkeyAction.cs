﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace nUpdate.Actions
{
    public class DeleteRegistrySubkeyAction : IUpdateAction
    {
        public string Name => "DeleteRegistrySubkey";
        public string Description => "Deletes a registry subkey.";
        public bool ExecuteBeforeReplacingFiles { get; set; }

        public string RegistryKey { get; set; }
        public IEnumerable<string> SubkeysToDelete { get; set; }

        public Task Execute()
        {
            return Task.Run(() =>
            {
                foreach (var subKey in SubkeysToDelete)
                {
                    RegistryManager.DeleteSubKey(RegistryKey, subKey);
                }
            });
        }
    }
}