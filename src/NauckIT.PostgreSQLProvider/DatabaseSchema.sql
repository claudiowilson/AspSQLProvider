-- PostgreSQL 8 Membership Provider Schema

CREATE TABLE test.user (
	"user_id"										character(36)			NOT NULL,
	"username"									character varying(255)	NOT NULL,
	"application_name"							character varying(255)	NOT NULL,
	"email"										character varying(128)	NULL,
	"comment"									character varying(128)	NULL,
	"password"									character varying(255)	NOT NULL,
	"password_question"							character varying(255)	NULL,
	"password_answer"							character varying(255)	NULL,
	"is_approved"								boolean					NULL, 
	"last_activity_date"							timestamptz				NULL,
	"last_login_date"								timestamptz				NULL,
	"last_password_change_date"					timestamptz				NULL,
	"creation_date"								timestamptz				NULL, 
	"is_online"									boolean					NULL,
	"is_locked_out"								boolean					NULL,
	"last_lock_out_date"							timestamptz				NULL,
	"failed_password_attempt_count"				integer					NULL,
	"failed_password_attempt_window_start"			timestamptz				NULL,
	"failed_password_answer_attempt_count"			integer					NULL,
	"failed_password_answer_attempt_window_start"	timestamptz				NULL,
	CONSTRAINT users_pkey PRIMARY KEY ("user_id"),
	CONSTRAINT users_username_application_unique UNIQUE ("username", "application_name")
);

CREATE INDEX users_email_index ON test.user ("email");
CREATE INDEX users_islockedout_index ON test.user ("is_locked_out");

-- PostgreSQL 8 Role Provider Schema

CREATE TABLE test.role (
	"role_name"				character varying(255)	NOT NULL,
	"application_name"		character varying(255)	NOT NULL,
	CONSTRAINT roles_pkey PRIMARY KEY ("role_name", "application_name")
);

CREATE TABLE test.user_in_role (
	"username"				character varying(255)	NOT NULL,
	"role_name"				character varying(255)	NOT NULL,
	"application_name"		character varying(255)	NOT NULL,
	CONSTRAINT usersinroles_pkey PRIMARY KEY ("username", "role_name", "application_name"),
	CONSTRAINT usersinroles_username_fkey FOREIGN KEY ("username", "application_name") REFERENCES test.user ("username", "application_name") ON DELETE CASCADE,
	CONSTRAINT usersinroles_rolename_fkey FOREIGN KEY ("role_name", "application_name") REFERENCES test.role ("role_name", "application_name") ON DELETE CASCADE
);

-- PostgreSQL 8 Profile Provider Schema

CREATE TABLE test.profile (
	"profile_id"					character(36)			NOT NULL,
	"username"				character varying(255)	NOT NULL,
	"application_name"		character varying(255)	NOT NULL,
	"is_anonymous"			boolean					NULL,
	"last_activity_date"		timestamptz				NULL,
	"last_updated_date"		timestamptz				NULL,
	CONSTRAINT profiles_pkey PRIMARY KEY ("profile_id"),
	CONSTRAINT profiles_username_application_unique UNIQUE ("username", "application_name"),
	CONSTRAINT profiles_username_fkey FOREIGN KEY ("username", "application_name") REFERENCES test.user ("username", "application_name") ON DELETE CASCADE
);

CREATE INDEX profiles_isanonymous_index ON test.profile ("is_anonymous");

CREATE TABLE test.profile_data (
	"profile_data_id"					character(36)			NOT NULL,
	"profile"				character(36)			NOT NULL,
	"name"					character varying(255)	NOT NULL,
	"value_string"			text					NULL,
	"value_binary"			bytea					NULL,
	CONSTRAINT profiledata_pkey PRIMARY KEY ("profile_data_id"),
	CONSTRAINT profiledata_profile_name_unique UNIQUE ("profile", "name"),
	CONSTRAINT profiledata_profile_fkey FOREIGN KEY ("profile") REFERENCES test.profile ("profile_id") ON DELETE CASCADE
);

-- PostgreSQL 8 Session-Store Provider Schema

CREATE TABLE test.session (
	"session_id"				character varying(80)	NOT NULL,
	"application_name"		character varying(255)	NOT NULL,
	"created"				timestamptz				NOT NULL,
	"expires"				timestamptz				NOT NULL,
	"timeout"				integer					NOT NULL,
	"locked"				boolean					NOT NULL,
	"lock_id"				integer					NOT NULL,
	"lock_date"				timestamptz				NOT NULL,
	"data"					text					NULL,
	"flag"					integer					NOT NULL,
	CONSTRAINT sessions_pkey PRIMARY KEY (session_id, application_name)
);