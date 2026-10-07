interface User {
	id: string;
	email: string;
	name: string;
}

let user = $state<User | null>(null);

export const auth = {
	get current() {
		return user;
	},
	login(u: User) {
		user = u;
	},
	logout() {
		user = null;
	},
	setUser(u: User | null) {
		user = u;
	}
};
