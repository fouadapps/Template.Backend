﻿using Template.Backend.Data.Audit.Configuration;
using Template.Backend.Data.Configuration;
using Template.Backend.Model;
using Template.Backend.Model.Audit;
using Template.Backend.Model.Audit.Entities;
using Template.Backend.Model.Entities;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Template.Backend.Data
{
    class StarterDbContext : DbContext, IDbContext
    {
        // Audit
        public static Dictionary<Type, Type> _auditTypesMapping = new Dictionary<Type, Type>();

        public const string _Name = "StarterBackend";

        public StarterDbContext() : base(_Name)
        {
            Database.SetInitializer(new MigrateDatabaseToLatestVersion<StarterDbContext, Migrations.Configuration>());
            //Database.SetInitializer(new StarterSeedData());
        }

        /// <summary>
        /// Initializes the <see cref="StarterDbContext"/> class.
        /// </summary>
        static StarterDbContext()
        {
            MapAuditEntities();
        }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Department> Department { get; set; }

        // Audit
        public DbSet<CompanyAudit> CompanyAudit { get; set; }
        public DbSet<EmployeeAudit> EmployeeAudit { get; set; }
        public DbSet<DepartmentAudit> DepartmentAudit { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Configurations.Add(new CompanyConfiguration());
            modelBuilder.Configurations.Add(new EmployeeConfiguration());
            modelBuilder.Configurations.Add(new DepartmentConfiguration());

            // Audit
            modelBuilder.Configurations.Add(new CompanyAuditConfiguration());
            modelBuilder.Configurations.Add(new EmployeeAuditConfiguration());
            modelBuilder.Configurations.Add(new DepartmentAuditConfiguration());
        }

        /// <summary>
        /// Sets the specified entity state as modified.
        /// </summary>
        /// <param name="entity">The entity.</param>
        public void SetModified(object entity)
        {
            Entry(entity).State = EntityState.Modified;
        }

        /// <summary>
        /// Determines whether the specified entity is detached.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>
        ///   <c>true</c> if the specified entity is detached; otherwise, <c>false</c>.
        /// </returns>
        public bool IsDetached(object entity)
        {
            return Entry(entity).State == EntityState.Detached;
        }

        /// <summary>
        /// Audit operations and SaveChanges
        /// </summary>
        public int Commit(string userName)
        {
            // Audit Added entities after saveChanges for using autoGenerated id from database in audit table
            var addedEntities = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
            AuditTableEntites(userName);
            base.SaveChanges();
            AuditTableAddedEntites(addedEntities, userName);
            return base.SaveChanges();
        }

        /// <summary>
        /// Audit operations and asynchronous SaveChanges
        /// </summary>
        public Task<int> CommitAsync(string userName)
        {
            // Audit Added entities after saveChanges for using autoGenerated id from database in audit table
            var addedEntities = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
            AuditTableEntites(userName);
            base.SaveChanges();
            AuditTableAddedEntites(addedEntities, userName);
            return base.SaveChangesAsync();
        }

        /// <summary>
        /// Audit operations and asynchronous SaveChanges
        /// </summary>
        public override Task<int> SaveChangesAsync()
        {
            try
            {
                // Audit Added entities after saveChanges for using autoGenerated id from database in audit table
                var addedEntities = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
                AuditTableEntites();
                base.SaveChangesAsync().GetAwaiter().GetResult();
                AuditTableAddedEntites(addedEntities);
                return base.SaveChangesAsync();
            }
            catch (DbEntityValidationException e)
            {
                // Logging the DbEntityValidationException in a friendly way, like the following
                // ERROR Entity of type "Company" in state "Added" has the following validation errors:
                // ERROR - Property: "Name", Error: "Le champ Name est requis."
                foreach (var eve in e.EntityValidationErrors)
                {
                    Debug.WriteLine("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                        eve.Entry.Entity.GetType().Name, eve.Entry.State);
                    foreach (var ve in eve.ValidationErrors)
                    {
                        Debug.WriteLine("- Property: \"{0}\", Error: \"{1}\"", ve.PropertyName, ve.ErrorMessage);
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Audit operations and SaveChanges
        /// </summary>
        public override int SaveChanges()
        {
            try
            {
                // Audit Added entities after saveChanges for using autoGenerated id from database in audit table
                var addedEntities = ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList();
                AuditTableEntites();
                base.SaveChanges();
                AuditTableAddedEntites(addedEntities);
                return base.SaveChanges();
            }
            catch (DbEntityValidationException e)
            {
                // Logging the DbEntityValidationException in a friendly way, like the following
                // ERROR Entity of type "Company" in state "Added" has the following validation errors:
                // ERROR - Property: "Name", Error: "Le champ Name est requis."
                foreach (var eve in e.EntityValidationErrors)
                {
                    Debug.WriteLine("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                        eve.Entry.Entity.GetType().Name, eve.Entry.State);
                    foreach (var ve in eve.ValidationErrors)
                    {
                        Debug.WriteLine("- Property: \"{0}\", Error: \"{1}\"", ve.PropertyName, ve.ErrorMessage);
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Log Audit for entities that implement IEntity interface
        /// and mapped in auditTypesMapping Dictionary
        /// </summary>
        private void AuditTableEntites(string userName = null)
        {
            var CreatedDate = DateTime.Now;
            foreach (var entry in ChangeTracker.Entries<IEntity>())
            {
                var entityType = GetEntityType(entry.Entity.GetType());

                //EntityState.Added
                // Added entities are auditable after saveChanges
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.RowVersion = 1;
                    entry.Entity.CreatedOn = CreatedDate;
                }

                // make this check is better than instantiate a DbEntityEntry without use it when entry.State = Unchanged
                bool entryCheck = entry.State == EntityState.Modified || entry.State == EntityState.Deleted;

                if (_auditTypesMapping.ContainsKey(entityType) && entryCheck)
                {
                    Type auditType = _auditTypesMapping[entityType];
                    var auditProperties = auditType.GetProperties();
                    DbEntityEntry<IAuditEntity> auditEntityEntry = GetDbEntityEntry(auditType);

                    // EntityState.Modified
                    if (entry.State == EntityState.Modified)
                    {
                        var entityProperties = entry.CurrentValues.PropertyNames;

                        // GetDatabaseValues() of RowVersion prevent user to set a wrong value
                        entry.Entity.RowVersion = entry.GetDatabaseValues().GetValue<int>(nameof(entry.Entity.RowVersion)) + 1;
                        entry.Entity.CreatedOn = entry.GetDatabaseValues().GetValue<DateTime?>(nameof(entry.Entity.CreatedOn));

                        foreach (var property in auditProperties)
                        {
                            if (entityProperties.Contains(property.Name))
                            {
                                auditEntityEntry.Property(property.Name).CurrentValue = entry.Property(property.Name).CurrentValue;
                            }
                        }
                        auditEntityEntry.Entity.CreatedDate = CreatedDate;
                        auditEntityEntry.Entity.AuditOperation = AuditOperations.UPDATE;
                        auditEntityEntry.Entity.RowVersion = entry.Entity.RowVersion;
                        auditEntityEntry.Entity.LoggedUserName = userName;
                    }

                    // EntityState.Deleted
                    if (entry.State == EntityState.Deleted)
                    {
                        var entityProperties = entry.OriginalValues.PropertyNames;

                        foreach (var property in auditProperties)
                        {
                            if (entityProperties.Contains(property.Name))
                            {
                                auditEntityEntry.Property(property.Name).CurrentValue = entry.Property(property.Name).OriginalValue;
                            }
                        }
                        auditEntityEntry.Entity.AuditOperation = AuditOperations.DELETE;
                        auditEntityEntry.Entity.CreatedDate = CreatedDate;
                        auditEntityEntry.Entity.RowVersion = entry.Property<int>(nameof(auditEntityEntry.Entity.RowVersion)).OriginalValue;
                        auditEntityEntry.Entity.LoggedUserName = userName;
                    }
                }
            }
        }

        private void AuditTableAddedEntites(IEnumerable<DbEntityEntry> AddedEntites, string userName = null)
        {
            var CreatedDate = DateTime.Now;
            foreach (var entry in AddedEntites)
            {
                var entityType = GetEntityType(entry.Entity.GetType());
                if (_auditTypesMapping.ContainsKey(entityType))
                {
                    Type auditType = _auditTypesMapping[entityType];
                    var auditProperties = auditType.GetProperties();
                    DbEntityEntry<IAuditEntity> auditEntityEntry = GetDbEntityEntry(auditType);

                    var entityProperties = entry.CurrentValues.PropertyNames;

                    foreach (var property in auditProperties)
                    {
                        if (entityProperties.Contains(property.Name))
                        {
                            auditEntityEntry.Property(property.Name).CurrentValue = entry.Property(property.Name).CurrentValue;
                        }
                    }
                    auditEntityEntry.Entity.AuditOperation = AuditOperations.INSERT;
                    auditEntityEntry.Entity.RowVersion = 1;
                    auditEntityEntry.Entity.LoggedUserName = userName;
                    auditEntityEntry.Entity.CreatedDate = CreatedDate;
                }
            }
        }

        /// <summary>
        /// if is a proxie class get baseType
        /// </summary>
        /// <param name="type">Type</param>
        /// <returns></returns>
        private static Type GetEntityType(Type type)
        {
            return ObjectContext.GetObjectType(type);
        }

        /// <summary>
        /// get DbEntityEntry instance from Type
        /// </summary>
        /// <param name="auditType">Type</param>
        /// <returns></returns>
        private DbEntityEntry<IAuditEntity> GetDbEntityEntry(Type auditType)
        {
            DbSet set = this.Set(auditType);
            IAuditEntity auditEntity = set.Create() as IAuditEntity;
            set.Add(auditEntity);
            return this.Entry(auditEntity);
        }

        private static void TryAdd(Type key, Type value)
        {
            if (!_auditTypesMapping.ContainsKey(key))
                _auditTypesMapping.Add(key, value);
        }

        private static void MapAuditEntities()
        {
            TryAdd(typeof(Company), typeof(CompanyAudit));
            TryAdd(typeof(Employee), typeof(EmployeeAudit));
            TryAdd(typeof(Department), typeof(DepartmentAudit));
        }
    }
}