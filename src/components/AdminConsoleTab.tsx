import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { 
  Users, 
  UserPlus, 
  Search, 
  Shield, 
  Mail, 
  UserCheck, 
  UserX, 
  CheckCircle, 
  XCircle, 
  Settings, 
  Check, 
  AlertCircle
} from 'lucide-react';
import { User } from '../types';
import { mockFetch } from '../mockApi';

interface AdminConsoleTabProps {
  onTriggerLog?: (code: string, severity: 'Info' | 'Warning' | 'Major' | 'Critical', module: string, user: string, desc: string) => void;
  currentUser: User | null;
}

export const AdminConsoleTab: React.FC<AdminConsoleTabProps> = ({ onTriggerLog, currentUser }) => {
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [searchQuery, setSearchQuery] = useState<string>('');
  
  // New user form state
  const [newFullName, setNewFullName] = useState<string>('');
  const [newEmail, setNewEmail] = useState<string>('');
  const [newRole, setNewRole] = useState<"Admin" | "Editor" | "Advertiser">('Editor');
  const [formError, setFormError] = useState<string | null>(null);
  const [formSuccess, setFormSuccess] = useState<string | null>(null);

  // Edit states
  const [editingUserId, setEditingUserId] = useState<string | null>(null);
  const [editRole, setEditRole] = useState<"Admin" | "Editor" | "Advertiser">('Editor');
  const [editStatus, setEditStatus] = useState<"Active" | "Suspended">('Active');

  // Load all users from REST API
  const fetchUsers = async () => {
    setLoading(true);
    try {
      const res = await mockFetch('/api/users');
      const data = await res.json();
      if (res.ok) {
        setUsers(data);
      }
    } catch (err) {
      console.error("Error loading users:", err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUsers();
  }, []);

  // Handle Add User submission
  const handleAddUser = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    setFormSuccess(null);

    if (!newFullName.trim() || !newEmail.trim()) {
      setFormError("Full Name and Email Address are strictly required.");
      return;
    }

    // Basic email validation
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(newEmail)) {
      setFormError("Please enter a valid email address (e.g., test@afrobotics.co.za).");
      return;
    }

    try {
      const res = await mockFetch('/api/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: newFullName.trim(),
          email: newEmail.trim().toLowerCase(),
          role: newRole,
          accountStatus: 'Active'
        })
      });

      const data = await res.json();
      if (!res.ok) {
        setFormError(data.error || "Failed to create user profile.");
        return;
      }

      setFormSuccess(`Profile successfully created for ${newFullName}!`);
      setNewFullName('');
      setNewEmail('');
      setNewRole('Editor');
      
      // Refresh user database
      fetchUsers();

      if (onTriggerLog) {
        onTriggerLog(
          "USER_ADMIN_ADD", 
          "Info", 
          "IdentityGateway", 
          currentUser?.email || "admin@afrobotics.co.za", 
          `Created user profile for "${newFullName.trim()}" (${newRole}).`
        );
      }
    } catch (err) {
      console.error(err);
      setFormError("API communications protocol error.");
    }
  };

  // Handle Status Toggle (Activate/Suspend)
  const handleToggleStatus = async (userToUpdate: User) => {
    const nextStatus = userToUpdate.accountStatus === 'Active' ? 'Suspended' : 'Active';
    
    // Prevent suspending oneself
    if (currentUser && currentUser.id === userToUpdate.id && nextStatus === 'Suspended') {
      alert("Security Constraint: You are forbidden from suspending your own administrative account.");
      return;
    }

    try {
      const res = await mockFetch('/api/users/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: userToUpdate.id,
          accountStatus: nextStatus
        })
      });

      if (res.ok) {
        fetchUsers();
        if (onTriggerLog) {
          onTriggerLog(
            "USER_STATUS_CHANGE", 
            "Warning", 
            "IdentityGateway", 
            currentUser?.email || "admin@afrobotics.co.za", 
            `Changed status of ${userToUpdate.fullName} to ${nextStatus}.`
          );
        }
      }
    } catch (err) {
      console.error(err);
    }
  };

  // Handle Save edits (Role/Status)
  const handleSaveEdits = async (userId: string) => {
    try {
      const res = await mockFetch('/api/users/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: userId,
          role: editRole,
          accountStatus: editStatus
        })
      });

      if (res.ok) {
        setEditingUserId(null);
        fetchUsers();
        if (onTriggerLog) {
          onTriggerLog(
            "USER_ROLE_CHANGE", 
            "Info", 
            "IdentityGateway", 
            currentUser?.email || "admin@afrobotics.co.za", 
            `Updated profile config of user ID: ${userId}.`
          );
        }
      }
    } catch (err) {
      console.error(err);
    }
  };

  const startEditing = (userItem: User) => {
    setEditingUserId(userItem.id);
    setEditRole(userItem.role);
    setEditStatus(userItem.accountStatus);
  };

  // Filter users based on query
  const filteredUsers = users.filter(u => 
    u.fullName.toLowerCase().includes(searchQuery.toLowerCase()) ||
    u.email.toLowerCase().includes(searchQuery.toLowerCase()) ||
    u.role.toLowerCase().includes(searchQuery.toLowerCase()) ||
    u.accountStatus.toLowerCase().includes(searchQuery.toLowerCase())
  );

  // Stats calculation
  const totalUsersCount = users.length;
  const adminCount = users.filter(u => u.role === 'Admin').length;
  const editorCount = users.filter(u => u.role === 'Editor').length;
  const advertiserCount = users.filter(u => u.role === 'Advertiser').length;
  const suspendedCount = users.filter(u => u.accountStatus === 'Suspended').length;

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="space-y-8"
      key="admin_console_tab"
      id="admin_console_view"
    >
      {/* EXPLANATORY HEADER BANNER */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex items-start gap-4">
            <div className="p-3 bg-blue-50 text-blue-600 rounded-xl">
              <Users className="h-6 w-6" />
            </div>
            <div>
              <h2 className="text-xl font-extrabold font-display text-slate-900 tracking-tight">
                User Directory &amp; Role-Based Access Control (RBAC)
              </h2>
              <p className="text-sm text-slate-500 mt-1">
                Centralized platform administration. Manage user accounts, define system roles (Admin, Editor, Advertiser), and enforce account suspensions.
              </p>
            </div>
          </div>
          <div className="text-xs font-mono bg-slate-100 text-slate-600 px-3 py-1.5 rounded-lg border border-slate-200 self-start md:self-center">
            Logged in as: <strong className="text-slate-800">{currentUser?.fullName || "Sabelo Nkosi"}</strong> ({currentUser?.role || "Admin"})
          </div>
        </div>
      </div>

      {/* METRICS ROW */}
      <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
        {[
          { label: "Total Users", val: totalUsersCount, sub: "Registered accounts", color: "text-slate-900" },
          { label: "Administrators", val: adminCount, sub: "Full system control", color: "text-blue-600 font-bold" },
          { label: "Editors / Approvers", val: editorCount, sub: "QA visual workflow", color: "text-indigo-600" },
          { label: "Advertisers", val: advertiserCount, sub: "Campaign & assets", color: "text-emerald-600" },
          { label: "Suspended", val: suspendedCount, sub: "Forbidden logins", color: suspendedCount > 0 ? "text-rose-600 font-bold animate-pulse" : "text-slate-400" },
        ].map((m, idx) => (
          <div key={idx} className="bg-white border border-slate-200/80 rounded-xl p-4 shadow-2xs">
            <p className="text-[10px] uppercase font-bold text-slate-400 tracking-wider font-mono">{m.label}</p>
            <p className={`text-2xl font-extrabold tracking-tight mt-1 ${m.color}`}>{m.val}</p>
            <p className="text-[10px] text-slate-500 mt-0.5">{m.sub}</p>
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* USERS TABLE & SEARCH - LEFT 2 COLS */}
        <div className="lg:col-span-2 space-y-6">
          <div className="bg-white border border-slate-200/90 rounded-2xl shadow-sm overflow-hidden">
            {/* Header and Search */}
            <div className="p-5 border-b border-slate-200 bg-slate-50/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
              <h3 className="text-sm font-extrabold text-slate-800 uppercase tracking-widest font-display">
                Registered Operators
              </h3>
              <div className="relative w-full md:w-72">
                <Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                <input
                  type="text"
                  placeholder="Search user, email or role..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full pl-9 pr-4 py-2 bg-white border border-slate-200 rounded-lg text-xs font-medium focus:outline-hidden focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all placeholder:text-slate-400"
                />
              </div>
            </div>

            {/* List Table */}
            {loading ? (
              <div className="p-12 text-center text-slate-400 text-xs font-mono">
                <div className="animate-spin h-6 w-6 border-2 border-blue-600 border-t-transparent rounded-full mx-auto mb-3"></div>
                Loading secure directory database...
              </div>
            ) : filteredUsers.length === 0 ? (
              <div className="p-12 text-center text-slate-400 text-xs font-mono">
                No user profiles match your search filter criteria.
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left border-collapse">
                  <thead>
                    <tr className="border-b border-slate-200 bg-slate-50/20 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">
                      <th className="p-4 pl-6">Full Name &amp; Email</th>
                      <th className="p-4">Assigned Role</th>
                      <th className="p-4">Account Status</th>
                      <th className="p-4">Last Activity</th>
                      <th className="p-4 pr-6 text-right">Administrative Action</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 text-xs">
                    {filteredUsers.map((item) => {
                      const isEditing = editingUserId === item.id;
                      
                      // Role Color maps
                      const roleStyles = {
                        Admin: "bg-blue-50 text-blue-700 border-blue-200",
                        Editor: "bg-indigo-50 text-indigo-700 border-indigo-200",
                        Advertiser: "bg-emerald-50 text-emerald-700 border-emerald-200"
                      };

                      return (
                        <tr key={item.id} className="hover:bg-slate-50/30 transition-colors">
                          <td className="p-4 pl-6">
                            <div className="font-semibold text-slate-900">{item.fullName}</div>
                            <div className="text-slate-400 font-mono text-[11px] mt-0.5 flex items-center gap-1">
                              <Mail className="h-3 w-3 text-slate-300" />
                              {item.email}
                            </div>
                          </td>
                          <td className="p-4">
                            {isEditing ? (
                              <select
                                value={editRole}
                                onChange={(e) => setEditRole(e.target.value as any)}
                                className="px-2 py-1 bg-white border border-slate-200 rounded-md text-xs font-medium focus:outline-hidden focus:ring-1 focus:ring-blue-500"
                              >
                                <option value="Admin">Admin</option>
                                <option value="Editor">Editor</option>
                                <option value="Advertiser">Advertiser</option>
                              </select>
                            ) : (
                              <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-md border text-[10px] font-bold tracking-tight uppercase ${roleStyles[item.role]}`}>
                                <Shield className="h-3 w-3" />
                                {item.role}
                              </span>
                            )}
                          </td>
                          <td className="p-4">
                            {isEditing ? (
                              <select
                                value={editStatus}
                                onChange={(e) => setEditStatus(e.target.value as any)}
                                className="px-2 py-1 bg-white border border-slate-200 rounded-md text-xs font-medium focus:outline-hidden focus:ring-1 focus:ring-blue-500"
                              >
                                <option value="Active">Active</option>
                                <option value="Suspended">Suspended</option>
                              </select>
                            ) : (
                              <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold ${
                                item.accountStatus === 'Active' 
                                  ? 'bg-green-50 text-green-700 border border-green-200' 
                                  : 'bg-red-50 text-red-700 border border-red-200'
                              }`}>
                                {item.accountStatus === 'Active' ? (
                                  <CheckCircle className="h-3 w-3 text-green-500" />
                                ) : (
                                  <XCircle className="h-3 w-3 text-red-500" />
                                )}
                                {item.accountStatus}
                              </span>
                            )}
                          </td>
                          <td className="p-4 text-slate-500 font-mono text-[11px]">
                            {item.lastLoginAt ? (
                              new Date(item.lastLoginAt).toLocaleString()
                            ) : (
                              <span className="text-slate-300 italic">Never logged in</span>
                            )}
                          </td>
                          <td className="p-4 pr-6 text-right">
                            {isEditing ? (
                              <div className="flex justify-end gap-1.5">
                                <button
                                  onClick={() => handleSaveEdits(item.id)}
                                  className="inline-flex items-center gap-1 px-2 py-1 bg-green-600 hover:bg-green-500 text-white rounded-md font-semibold font-mono text-[10px]"
                                >
                                  <Check className="h-3.5 w-3.5" />
                                  SAVE
                                </button>
                                <button
                                  onClick={() => setEditingUserId(null)}
                                  className="inline-flex items-center gap-1 px-2 py-1 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded-md font-semibold font-mono text-[10px]"
                                >
                                  CANCEL
                                </button>
                              </div>
                            ) : (
                              <div className="flex justify-end gap-2">
                                <button
                                  onClick={() => startEditing(item)}
                                  className="inline-flex items-center gap-1 px-2.5 py-1.5 bg-slate-50 hover:bg-slate-150 text-slate-600 rounded-lg font-bold border border-slate-200 transition-colors"
                                >
                                  <Settings className="h-3.5 w-3.5 text-slate-400" />
                                  Edit Config
                                </button>
                                <button
                                  onClick={() => handleToggleStatus(item)}
                                  className={`inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg font-bold border transition-colors ${
                                    item.accountStatus === 'Active' 
                                      ? 'bg-rose-50 hover:bg-rose-100 text-rose-600 border-rose-200' 
                                      : 'bg-green-50 hover:bg-green-100 text-green-600 border-green-200'
                                  }`}
                                >
                                  {item.accountStatus === 'Active' ? (
                                    <>
                                      <UserX className="h-3.5 w-3.5 text-rose-500" />
                                      Suspend
                                    </>
                                  ) : (
                                    <>
                                      <UserCheck className="h-3.5 w-3.5 text-green-500" />
                                      Activate
                                    </>
                                  )}
                                </button>
                              </div>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>

        {/* CREATE PROFILE FORM - RIGHT 1 COL */}
        <div className="space-y-6">
          <div className="bg-white border border-slate-200/90 rounded-2xl p-5 shadow-sm">
            <div className="flex items-center gap-2 mb-4 pb-3 border-b border-slate-100">
              <UserPlus className="h-5 w-5 text-blue-600" />
              <h3 className="text-sm font-extrabold text-slate-800 uppercase tracking-widest font-display">
                Create User Profile
              </h3>
            </div>

            <form onSubmit={handleAddUser} className="space-y-4">
              {formError && (
                <div className="p-3 bg-red-50 border border-red-200 text-red-700 text-xs rounded-lg flex items-start gap-2 animate-shake">
                  <AlertCircle className="h-4 w-4 shrink-0 mt-0.5 text-red-500" />
                  <p>{formError}</p>
                </div>
              )}

              {formSuccess && (
                <div className="p-3 bg-green-50 border border-green-200 text-green-700 text-xs rounded-lg flex items-start gap-2">
                  <CheckCircle className="h-4 w-4 shrink-0 mt-0.5 text-green-500" />
                  <p>{formSuccess}</p>
                </div>
              )}

              <div>
                <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
                  Full Operator Name *
                </label>
                <input
                  type="text"
                  placeholder="e.g. Sindi Dube"
                  value={newFullName}
                  onChange={(e) => setNewFullName(e.target.value)}
                  className="w-full px-3 py-2 bg-slate-50/50 border border-slate-200 rounded-lg text-xs font-medium focus:outline-hidden focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all placeholder:text-slate-300"
                />
              </div>

              <div>
                <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
                  Corporate Email Address *
                </label>
                <input
                  type="email"
                  placeholder="e.g. sindi@afrobotics.co.za"
                  value={newEmail}
                  onChange={(e) => setNewEmail(e.target.value)}
                  className="w-full px-3 py-2 bg-slate-50/50 border border-slate-200 rounded-lg text-xs font-medium focus:outline-hidden focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all placeholder:text-slate-300"
                />
              </div>

              <div>
                <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
                  Strategic Business Role *
                </label>
                <select
                  value={newRole}
                  onChange={(e) => setNewRole(e.target.value as any)}
                  className="w-full px-3 py-2 bg-slate-50/50 border border-slate-200 rounded-lg text-xs font-medium focus:outline-hidden focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
                >
                  <option value="Editor">Editor (QA Approvals / Operator)</option>
                  <option value="Admin">Admin (Full Central Access)</option>
                  <option value="Advertiser">Advertiser (Campaign &amp; Asset Planning)</option>
                </select>
              </div>

              <div className="pt-2">
                <button
                  type="submit"
                  className="w-full py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-bold rounded-lg text-xs tracking-tight shadow-sm hover:shadow-blue-500/15 transition-all cursor-pointer flex items-center justify-center gap-2"
                >
                  <UserPlus className="h-4 w-4" />
                  PROVISION NEW USER
                </button>
              </div>
            </form>

            <div className="mt-5 pt-4 border-t border-slate-100 space-y-2">
              <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">
                Mock Password Defaults:
              </h4>
              <p className="text-[10px] text-slate-500 leading-relaxed">
                New accounts automatically accept simulated credentials for login:
                <br />
                • Admin emails use password <code className="bg-slate-100 text-slate-800 px-1 py-0.5 rounded font-mono">admin123</code>
                <br />
                • Editor emails use password <code className="bg-slate-100 text-slate-800 px-1 py-0.5 rounded font-mono">editor123</code>
                <br />
                • Advertiser emails use password <code className="bg-slate-100 text-slate-800 px-1 py-0.5 rounded font-mono">adv123</code>
              </p>
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  );
};
