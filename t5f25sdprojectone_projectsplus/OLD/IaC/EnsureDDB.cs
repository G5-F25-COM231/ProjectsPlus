namespace t5f25sdprojectone_projectsplus.OLD.IaC
{
    public class EnsureDDB
    {
        // EnsureDDB(parameters = all requirements to EnsureDDB, like creds, provider modules, ids of other resources, network(vpc), security_group, subnet_goup, etc)

        // EnsureDDB_CreateAsync()
        /*
         * - logically sequence and creates the infra, if it does not exist
         * - stringify and log the metadata of all infra created here, with the logger, to the .txt file
         * - calls EnsureDDB_Exists() - as needed
         * - returns the summary of what was created    
        */



        // EnsureDDB_DeleteAsync() 
        /*
         * -reads the logs file for logs from here, gets ids and such from the and logically sequence delete all that was created and then remove the log line from the log file.
         * - calls EnsureDDB_Exists() - as needed
         * - returns the summery of what was destroyed      
        */


        // EnsureDDB_ExistsAsync() 
        /*
         * - reads the logs and and checks inthe cloud if the engendered infra here exists
         *- returns a summary of what exists and what is meissing, of the engendered.      
        */
    }
}
